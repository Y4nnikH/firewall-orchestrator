using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using FWO.Api.Client;
using FWO.Api.Client.Queries;
using FWO.Basics;
using FWO.Data;
using FWO.Data.Modelling;
using FWO.Middleware.Server.Controllers;
using FWO.Middleware.Server.Requests;
using FWO.Middleware.Server.Responses;
using FWO.Middleware.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NUnit.Framework;

namespace FWO.Test;

[TestFixture]
internal class ApplicationZonesControllerTest
{
    private static readonly List<string> kControllerRoutes = new() { "api/modelling" };
    private static readonly List<string> kModellerRole = new() { Roles.Modeller };

    [Test]
    public void GetUsesApplicationZonesRoute()
    {
        RouteAttribute[] controllerRoutes = typeof(ApplicationZonesController).GetCustomAttributes<RouteAttribute>().ToArray();
        MethodInfo getMethod = typeof(ApplicationZonesController).GetMethod(nameof(ApplicationZonesController.Get))!;
        HttpPostAttribute? httpPost = getMethod.GetCustomAttribute<HttpPostAttribute>();

        Assert.Multiple(() =>
        {
            Assert.That(controllerRoutes.Select(route => route.Template), Is.EquivalentTo(kControllerRoutes));
            Assert.That(httpPost?.Template, Is.EqualTo("getIpDataForOwners"));
        });
    }

    [Test]
    public void GetAllowsEmptyRequestBodyAndRequiredRoles()
    {
        MethodInfo getMethod = typeof(ApplicationZonesController).GetMethod(nameof(ApplicationZonesController.Get))!;
        ParameterInfo requestParameter = getMethod.GetParameters().Single();
        FromBodyAttribute? fromBody = requestParameter.GetCustomAttribute<FromBodyAttribute>();
        AuthorizeAttribute? authorize = getMethod.GetCustomAttribute<AuthorizeAttribute>();

        Assert.Multiple(() =>
        {
            Assert.That(fromBody?.EmptyBodyBehavior, Is.EqualTo(EmptyBodyBehavior.Allow));
            Assert.That(authorize?.Roles, Is.EqualTo($"{Roles.Admin}, {Roles.Auditor}, {Roles.Modeller}"));
        });
    }

    [Test]
    public void ApplicationAddressQueryReadsActiveAppServerAddressesDirectly()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ModellingQueries.getApplicationIpAddresses, Does.Contain("query getApplicationIpAddresses"));
            Assert.That(ModellingQueries.getApplicationIpAddresses, Does.Contain("owner_network"));
            Assert.That(ModellingQueries.getApplicationIpAddresses, Does.Contain("nw_type: { _eq: 10 }"));
            Assert.That(ModellingQueries.getApplicationIpAddresses, Does.Contain("is_deleted: { _eq: false }"));
            Assert.That(ModellingQueries.getApplicationIpAddresses, Does.Contain("app_id: owner_id"));
            Assert.That(ModellingQueries.getApplicationIpAddresses, Does.Not.Contain("modelling_nwgroup"));
        });
    }

    [Test]
    public async Task GetReturnsUndeletedAppServerAddressesWithoutAnApplicationZone()
    {
        ApplicationAddressesApiConnection apiConnection = new()
        {
            Owners = CreateOwners((7, "Application 07", "APP-7"), (8, "Application 08", "APP-8")),
            AppServers = CreateAppServers(
                (7, "10.7.0.1/32", "10.7.0.1/32"),
                (7, "10.7.0.0/24", "10.7.0.255/24"),
                (8, "10.8.0.1", "10.8.0.9"),
                (9, "10.9.0.1", ""))
        };
        ApplicationZonesController controller = CreateController(apiConnection, PrincipalWithRoles(Roles.Admin));

        ActionResult<List<ApplicationAddressResponse>> result = await controller.Get(new GetApplicationZonesRequest());

        List<ApplicationAddressResponse> response = (List<ApplicationAddressResponse>)((OkObjectResult)result.Result!).Value!;
        Assert.Multiple(() =>
        {
            Assert.That(apiConnection.Queries, Is.EqualTo(new List<string>
            {
                OwnerQueries.getOwnersFiltered,
                ModellingQueries.getApplicationIpAddresses
            }));
            Assert.That(SerializeVariables(apiConnection.LastApplicationAddressVariables),
                Does.Contain("\"owner_id\":{\"_in\":[7,8]}"));
            Assert.That(response, Has.Count.EqualTo(2));
            Assert.That(response[0].ApplicationId, Is.EqualTo(7));
            Assert.That(response[0].ApplicationName, Is.EqualTo("Application 07"));
            Assert.That(response[0].AppIdExternal, Is.EqualTo("APP-7"));
            Assert.That(response[0].Addresses, Is.EqualTo(new List<string> { "10.7.0.1", "10.7.0.0/24" }));
            Assert.That(response[1].Addresses, Is.EqualTo(new List<string> { "10.8.0.1-10.8.0.9" }));
        });
    }

    [Test]
    public async Task GetReturnsSelectedApplicationsWithNoAddresses()
    {
        ApplicationAddressesApiConnection apiConnection = new()
        {
            Owners = CreateOwners((7, "Application 07", "APP-7"))
        };
        ApplicationZonesController controller = CreateController(apiConnection, PrincipalWithRoles(Roles.Admin));

        ActionResult<List<ApplicationAddressResponse>> result = await controller.Get(new GetApplicationZonesRequest());

        List<ApplicationAddressResponse> response = (List<ApplicationAddressResponse>)((OkObjectResult)result.Result!).Value!;
        Assert.Multiple(() =>
        {
            Assert.That(response, Has.Count.EqualTo(1));
            Assert.That(response[0].ApplicationId, Is.EqualTo(7));
            Assert.That(response[0].Addresses, Is.Empty);
        });
    }

    [Test]
    public async Task GetExcludesDeletedAppServerAddresses()
    {
        ApplicationAddressesApiConnection apiConnection = new()
        {
            Owners = CreateOwners((7, "Application 07", "APP-7")),
            AppServers = CreateAppServers((7, "10.7.0.1", ""), (7, "10.7.0.2", ""))
        };
        apiConnection.AppServers[1].IsDeleted = true;
        ApplicationZonesController controller = CreateController(apiConnection, PrincipalWithRoles(Roles.Admin));

        ActionResult<List<ApplicationAddressResponse>> result = await controller.Get(new GetApplicationZonesRequest());

        List<ApplicationAddressResponse> response = (List<ApplicationAddressResponse>)((OkObjectResult)result.Result!).Value!;
        Assert.Multiple(() =>
        {
            Assert.That(response[0].Addresses, Is.EqualTo(new List<string> { "10.7.0.1" }));
            Assert.That(response[0].Addresses, Does.Not.Contain("10.7.0.2"));
        });
    }

    [Test]
    public async Task GetDoesNotReadAddressesWhenNoApplicationsMatch()
    {
        ApplicationAddressesApiConnection apiConnection = new();
        ApplicationZonesController controller = CreateController(apiConnection, PrincipalWithRoles(Roles.Admin));

        ActionResult<List<ApplicationAddressResponse>> result = await controller.Get(new GetApplicationZonesRequest());

        List<ApplicationAddressResponse> response = (List<ApplicationAddressResponse>)((OkObjectResult)result.Result!).Value!;
        Assert.Multiple(() =>
        {
            Assert.That(response, Is.Empty);
            Assert.That(apiConnection.Queries, Is.EqualTo(new List<string> { OwnerQueries.getOwnersFiltered }));
        });
    }

    [Test]
    public async Task GetRestrictsModellerAddressesToEditableApplications()
    {
        ApplicationAddressesApiConnection apiConnection = new()
        {
            Owners = CreateOwners((7, "Application 07", "APP-7")),
            AppServers = CreateAppServers((7, "10.7.0.1", ""))
        };
        ClaimsPrincipal modeller = PrincipalWithRolesAndClaims(
            kModellerRole, new Claim("x-hasura-editable-owners", "{7}"));
        ApplicationZonesController controller = CreateController(apiConnection, modeller);

        await controller.Get(new GetApplicationZonesRequest());

        Assert.That(SerializeVariables(apiConnection.LastApplicationVariables), Does.Contain("\"id\":{\"_in\":[7]}"));
    }

    [Test]
    public async Task GetPassesApplicationFiltersAndPagingToTheOwnerQuery()
    {
        ApplicationAddressesApiConnection apiConnection = new()
        {
            Owners = CreateOwners((7, "Application 07", "APP-7"))
        };
        ApplicationZonesController controller = CreateController(apiConnection, PrincipalWithRoles(Roles.Admin));
        GetApplicationZonesRequest request = new()
        {
            Options = new()
            {
                Filter = new()
                {
                    ApplicationId = new() { 7, 8 },
                    ApplicationName = new() { "application *", "second application" },
                    AppIdExternal = new() { "app-*", "second-app" }
                },
                Limit = 20,
                Offset = 5
            }
        };

        await controller.Get(request);

        string variables = SerializeVariables(apiConnection.LastApplicationVariables);
        Assert.Multiple(() =>
        {
            Assert.That(variables, Does.Contain("\"id\":{\"_in\":[7,8]}"));
            Assert.That(variables, Does.Contain("\"name\":{\"_ilike\":\"application %\"}"));
            Assert.That(variables, Does.Contain("\"name\":{\"_ilike\":\"%second application%\"}"));
            Assert.That(variables, Does.Contain("\"app_id_external\":{\"_ilike\":\"app-%\"}"));
            Assert.That(variables, Does.Contain("\"app_id_external\":{\"_ilike\":\"%second-app%\"}"));
            Assert.That(variables, Does.Contain("\"limit\":20"));
            Assert.That(variables, Does.Contain("\"offset\":5"));
        });
    }

    [Test]
    public void ApplicationAddressVariablesContainOnlyTheVisibleApplicationIds()
    {
        List<int> applicationIds = new() { 7, 8 };

        string variables = SerializeVariables(ApplicationZoneQueryBuilder.BuildApplicationAddressVariables(applicationIds));

        Assert.Multiple(() =>
        {
            Assert.That(variables, Does.Contain("\"owner_id\":{\"_in\":[7,8]}"));
            Assert.That(variables, Does.Not.Contain("group_type"));
            Assert.That(variables, Does.Not.Contain("id_string"));
        });
    }

    [Test]
    public void RequestDefaultsOptionsToAnEmptyObject()
    {
        GetApplicationZonesRequest request = new();

        Assert.Multiple(() =>
        {
            Assert.That(request.Options, Is.Not.Null);
            Assert.That(request.Options!.Filter, Is.Null);
            Assert.That(request.Options.Limit, Is.Null);
            Assert.That(request.Options.Offset, Is.Null);
            Assert.That(request.Options.ShowOnlyActiveState, Is.Null);
        });
    }

    [Test]
    public void RequestDeserializesMultiValueFiltersAndRejectsRemovedZoneSpecificProperties()
    {
        const string ValidJson = """{"options":{"filter":{"applicationId":[7,8],"applicationName":["App 7","App 8"],"appIdExternal":["APP-7","APP-8"]}}}""";
        const string InvalidJson = """{"options":{"details-level":"ip-only","filter":{"id":7}}}""";

        GetApplicationZonesRequest? request = JsonSerializer.Deserialize<GetApplicationZonesRequest>(ValidJson);

        Assert.Multiple(() =>
        {
            Assert.That(request?.Options?.Filter?.ApplicationId, Is.EqualTo(new List<int> { 7, 8 }));
            Assert.That(request?.Options?.Filter?.ApplicationName, Is.EqualTo(new List<string> { "App 7", "App 8" }));
            Assert.That(request?.Options?.Filter?.AppIdExternal, Is.EqualTo(new List<string> { "APP-7", "APP-8" }));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GetApplicationZonesRequest>(InvalidJson));
        });
    }

    [Test]
    public async Task GetAggregatesAllKnownValidationErrors()
    {
        ApplicationZonesController controller = CreateController(new ApplicationAddressesApiConnection(), PrincipalWithRoles(Roles.Admin));
        GetApplicationZonesRequest request = new()
        {
            Options = new()
            {
                Filter = new()
                {
                    ApplicationId = new() { 0 },
                    ApplicationName = new() { "bad\u0001application" },
                    AppIdExternal = new() { new string('a', GetMaxFilterTextLength() + 1) }
                },
                Limit = 0,
                Offset = -1
            }
        };

        ActionResult<List<ApplicationAddressResponse>> result = await controller.Get(request);
        ValidationProblemDetails validationProblem = (ValidationProblemDetails)((ObjectResult)result.Result!).Value!;

        Assert.Multiple(() =>
        {
            Assert.That(validationProblem.Errors.Keys, Does.Contain("options.filter.applicationId[0]"));
            Assert.That(validationProblem.Errors.Keys, Does.Contain("options.filter.applicationName[0]"));
            Assert.That(validationProblem.Errors.Keys, Does.Contain("options.filter.appIdExternal[0]"));
            Assert.That(validationProblem.Errors.Keys, Does.Contain("options.limit"));
            Assert.That(validationProblem.Errors.Keys, Does.Contain("options.offset"));
        });
    }

    [Test]
    public async Task GetRejectsNullOptions()
    {
        ApplicationZonesController controller = CreateController(new ApplicationAddressesApiConnection(), PrincipalWithRoles(Roles.Admin));
        GetApplicationZonesRequest request = new() { Options = null };

        ActionResult<List<ApplicationAddressResponse>> result = await controller.Get(request);

        ValidationProblemDetails validationProblem = (ValidationProblemDetails)((ObjectResult)result.Result!).Value!;
        Assert.That(validationProblem.Errors.Keys, Does.Contain("options"));
    }

    [Test]
    public async Task GetRejectsNullTextFilterValues()
    {
        const string RequestJson = """{"options":{"filter":{"applicationName":[null]}}}""";
        ApplicationZonesController controller = CreateController(new ApplicationAddressesApiConnection(), PrincipalWithRoles(Roles.Admin));
        GetApplicationZonesRequest request = JsonSerializer.Deserialize<GetApplicationZonesRequest>(RequestJson)!;

        ActionResult<List<ApplicationAddressResponse>> result = await controller.Get(request);

        ValidationProblemDetails validationProblem = (ValidationProblemDetails)((ObjectResult)result.Result!).Value!;
        Assert.That(validationProblem.Errors.Keys, Does.Contain("options.filter.applicationName[0]"));
    }

    private static List<FwoOwner> CreateOwners(params (int Id, string Name, string? AppIdExternal)[] owners)
    {
        return owners.Select(owner => new FwoOwner
        {
            Id = owner.Id,
            Name = owner.Name,
            ExtAppId = owner.AppIdExternal
        }).ToList();
    }

    private static List<ModellingAppServer> CreateAppServers(params (int AppId, string Ip, string IpEnd)[] appServers)
    {
        return appServers.Select((appServer, index) => new ModellingAppServer
        {
            Id = index + 1,
            AppId = appServer.AppId,
            Ip = appServer.Ip,
            IpEnd = appServer.IpEnd
        }).ToList();
    }

    private static ApplicationZonesController CreateController(ApiConnection apiConnection, ClaimsPrincipal user)
    {
        return new ApplicationZonesController(apiConnection)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            }
        };
    }

    private static ClaimsPrincipal PrincipalWithRoles(params string[] roles)
    {
        return PrincipalWithRolesAndClaims(roles);
    }

    private static ClaimsPrincipal PrincipalWithRolesAndClaims(IEnumerable<string> roles, params Claim[] claims)
    {
        IEnumerable<Claim> roleClaims = roles.Select(role => new Claim(ClaimTypes.Role, role));
        ClaimsIdentity identity = new(roleClaims.Concat(claims), "test", ClaimTypes.Name, ClaimTypes.Role);
        return new ClaimsPrincipal(identity);
    }

    private static string SerializeVariables(object? variables)
    {
        return JsonSerializer.Serialize(variables);
    }

    private static int GetMaxFilterTextLength()
    {
        FieldInfo constant = typeof(ApplicationZonesController).GetField(
            "kMaxFilterTextLength", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (int)constant.GetRawConstantValue()!;
    }

    private sealed class ApplicationAddressesApiConnection : SimulatedApiConnection
    {
        public List<FwoOwner> Owners { get; set; } = [];
        public List<ModellingAppServer> AppServers { get; set; } = [];
        public List<string> Queries { get; } = [];
        public object? LastApplicationVariables { get; private set; }
        public object? LastApplicationAddressVariables { get; private set; }

        public override Task<QueryResponseType> SendQueryAsync<QueryResponseType>(
            string query,
            object? variables = null,
            string? operationName = null,
            QueryChunkingOptions? chunkingOptions = null)
        {
            Queries.Add(query);
            if (query == OwnerQueries.getOwnersFiltered)
            {
                LastApplicationVariables = variables;
                return Task.FromResult((QueryResponseType)(object)Owners);
            }
            if (query == ModellingQueries.getApplicationIpAddresses)
            {
                LastApplicationAddressVariables = variables;
                return Task.FromResult((QueryResponseType)(object)AppServers.Where(appServer => !appServer.IsDeleted).ToList());
            }

            return Task.FromResult((QueryResponseType)(object)new List<ModellingAppServer>());
        }
    }
}
