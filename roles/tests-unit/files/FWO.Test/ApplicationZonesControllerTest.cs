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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;

namespace FWO.Test;

[TestFixture]
internal class ApplicationZonesControllerTest
{
    private static readonly string[] kControllerRoutes = ["api/modelling"];
    private static readonly string[] kModellerRole = [Roles.Modeller];

    [Test]
    public void GetUsesApplicationZonesRoute()
    {
        RouteAttribute[] controllerRoutes = typeof(ApplicationZonesController).GetCustomAttributes<RouteAttribute>().ToArray();
        MethodInfo getMethod = typeof(ApplicationZonesController).GetMethod(nameof(ApplicationZonesController.Get))!;
        HttpPostAttribute? httpPost = getMethod.GetCustomAttribute<HttpPostAttribute>();

        Assert.Multiple(() =>
        {
            Assert.That(controllerRoutes.Select(route => route.Template), Is.EquivalentTo(kControllerRoutes));
            Assert.That(httpPost?.Template, Is.EqualTo("getApplicationZones"));
        });
    }

    [Test]
    public void GetAllowsAuditorAdminAndModeller()
    {
        MethodInfo getMethod = typeof(ApplicationZonesController).GetMethod(nameof(ApplicationZonesController.Get))!;
        AuthorizeAttribute? authorize = getMethod.GetCustomAttribute<AuthorizeAttribute>();

        Assert.That(authorize?.Roles, Is.EqualTo($"{Roles.Admin}, {Roles.Auditor}, {Roles.Modeller}"));
    }

    [Test]
    public async Task GetQueriesEveryRequestedApplicationAndMapsCompleteAddresses()
    {
        ApplicationZonesApiConnection apiConnection = new()
        {
            Owners = CreateOwners((7, "Application Seven", "APP-7"), (8, "Application Eight", "APP-8")),
            ZonesByApplicationId = new Dictionary<int, List<ModellingAppZone>>
            {
                [7] = [CreateApplicationZone(7, 70, "AZ-7", "az-7", "10.7.0.1", "10.7.0.9")],
                [8] = [CreateApplicationZone(8, 80, "AZ-8", "az-8", "10.8.0.1", string.Empty)]
            }
        };
        ApplicationZonesController controller = CreateController(apiConnection, PrincipalWithRoles(Roles.Admin));
        GetApplicationZonesRequest request = new() { ApplicationIds = [7, 8] };

        ActionResult<List<ApplicationZoneResponse>> result = await controller.Get(request);

        OkObjectResult okResult = (OkObjectResult)result.Result!;
        List<ApplicationZoneResponse> response = (List<ApplicationZoneResponse>)okResult.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(apiConnection.Queries, Has.Count.EqualTo(3));
            Assert.That(apiConnection.Queries.Take(2), Is.All.EqualTo(ModellingQueries.getAppZonesByAppId));
            Assert.That(apiConnection.Queries[2], Is.EqualTo(OwnerQueries.getOwnersFiltered));
            Assert.That(apiConnection.ApplicationIds, Is.EqualTo(request.ApplicationIds));
            Assert.That(apiConnection.SetBestRoleCount, Is.Zero);
            Assert.That(response, Has.Count.EqualTo(2));
            Assert.That(response[0].ApplicationId, Is.EqualTo(7));
            Assert.That(response[0].ApplicationName, Is.EqualTo("Application Seven"));
            Assert.That(response[0].AppIdExternal, Is.EqualTo("APP-7"));
            Assert.That(response[0].Addresses[0].Ip, Is.EqualTo("10.7.0.1"));
            Assert.That(response[0].Addresses[0].IpEnd, Is.EqualTo("10.7.0.9"));
            Assert.That(response[0].Addresses[0].ImportSource, Is.EqualTo("manual"));
            Assert.That(response[0].Addresses[0].CustomType, Is.EqualTo(4));
        });
    }

    [Test]
    public async Task GetRestrictsModellerToEditableApplications()
    {
        ApplicationZonesApiConnection apiConnection = new();
        ClaimsPrincipal modeller = PrincipalWithRolesAndClaims(
            kModellerRole, new Claim("x-hasura-editable-owners", "{7,8}"));
        ApplicationZonesController controller = CreateController(apiConnection, modeller);
        GetApplicationZonesRequest request = new() { ApplicationIds = [7, 9] };

        await controller.Get(request);

        Assert.That(apiConnection.ApplicationIds, Is.EqualTo(new List<int> { 7 }));
    }

    [Test]
    public async Task GetDoesNotRestrictAdminWithModellerRole()
    {
        ApplicationZonesApiConnection apiConnection = new();
        ClaimsPrincipal adminAndModeller = PrincipalWithRolesAndClaims(
            new List<string> { Roles.Admin, Roles.Modeller }, new Claim("x-hasura-editable-owners", "{7}"));
        ApplicationZonesController controller = CreateController(apiConnection, adminAndModeller);
        GetApplicationZonesRequest request = new() { ApplicationIds = [7, 9] };

        await controller.Get(request);

        Assert.That(apiConnection.ApplicationIds, Is.EqualTo(request.ApplicationIds));
    }

    [Test]
    public async Task GetAppliesEveryNullableResponseFilter()
    {
        ApplicationZonesApiConnection apiConnection = new()
        {
            Owners = CreateOwners((7, "Application Seven", "APP-7")),
            ZonesByApplicationId = new Dictionary<int, List<ModellingAppZone>>
            {
                [7] =
                [
                    CreateApplicationZone(7, 70, "AZ-Match", "az-match", "10.7.0.1", string.Empty),
                    CreateApplicationZone(7, 71, "AZ-Other", "az-other", "10.7.0.2", string.Empty)
                ]
            }
        };
        ApplicationZonesController controller = CreateController(apiConnection, PrincipalWithRoles(Roles.Auditor));
        GetApplicationZonesRequest request = new()
        {
            ApplicationIds = [7],
            Options = new()
            {
                Filter = new()
                {
                    ApplicationId = 7,
                    ApplicationName = "application *",
                    AppIdExternal = "app-*",
                    Id = 70,
                    Name = "az-*",
                    IdString = "AZ-*",
                    IsDeleted = false
                }
            }
        };

        ActionResult<List<ApplicationZoneResponse>> result = await controller.Get(request);

        OkObjectResult okResult = (OkObjectResult)result.Result!;
        List<ApplicationZoneResponse> response = (List<ApplicationZoneResponse>)okResult.Value!;
        Assert.That(response.Select(applicationZone => applicationZone.Id), Is.EqualTo(new List<long> { 70 }));
    }

    [Test]
    public void RequestDefaultsOptionsToAnEmptyObject()
    {
        GetApplicationZonesRequest request = new();

        Assert.Multiple(() =>
        {
            Assert.That(request.ApplicationIds, Is.Null);
            Assert.That(request.Options, Is.Not.Null);
            Assert.That(request.Options!.Filter, Is.Null);
        });
    }

    [Test]
    public void RequestRejectsUnknownProperties()
    {
        const string Json = """{"applicationIds":[7],"unsupported":true}""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GetApplicationZonesRequest>(Json));
    }

    [Test]
    public async Task GetAggregatesAllKnownValidationErrors()
    {
        ApplicationZonesController controller = CreateController(new ApplicationZonesApiConnection(), PrincipalWithRoles(Roles.Admin));
        GetApplicationZonesRequest request = new()
        {
            ApplicationIds = [0, -2],
            Options = new()
            {
                Filter = new()
                {
                    ApplicationId = 0,
                    ApplicationName = "bad\u0001application",
                    AppIdExternal = new string('a', GetMaxFilterTextLength() + 1),
                    Id = 0,
                    Name = "bad\u0001name",
                    IdString = new string('a', GetMaxFilterTextLength() + 1)
                }
            }
        };

        ActionResult<List<ApplicationZoneResponse>> result = await controller.Get(request);
        ValidationProblemDetails validationProblem = (ValidationProblemDetails)((ObjectResult)result.Result!).Value!;

        Assert.Multiple(() =>
        {
            Assert.That(validationProblem.Errors.Keys, Does.Contain("applicationIds[0]"));
            Assert.That(validationProblem.Errors.Keys, Does.Contain("applicationIds[1]"));
            Assert.That(validationProblem.Errors.Keys, Does.Contain("options.filter.applicationId"));
            Assert.That(validationProblem.Errors.Keys, Does.Contain("options.filter.applicationName"));
            Assert.That(validationProblem.Errors.Keys, Does.Contain("options.filter.appIdExternal"));
            Assert.That(validationProblem.Errors.Keys, Does.Contain("options.filter.id"));
            Assert.That(validationProblem.Errors.Keys, Does.Contain("options.filter.name"));
            Assert.That(validationProblem.Errors.Keys, Does.Contain("options.filter.idString"));
        });
    }

    [Test]
    public async Task GetReturnsAllApplicationZonesForEmptyRequestBody()
    {
        ApplicationZonesApiConnection apiConnection = new()
        {
            Owners = CreateOwners((7, "Application Seven", "APP-7"), (8, "Application Eight", "APP-8")),
            AllApplicationZones =
            [
                CreateApplicationZone(7, 70, "AZ-7", "az-7", "10.7.0.1", string.Empty),
                CreateApplicationZone(8, 80, "AZ-8", "az-8", "10.8.0.1", string.Empty)
            ]
        };
        ApplicationZonesController controller = CreateController(apiConnection, PrincipalWithRoles(Roles.Admin));

        ActionResult<List<ApplicationZoneResponse>> result = await controller.Get(null);

        OkObjectResult okResult = (OkObjectResult)result.Result!;
        List<ApplicationZoneResponse> response = (List<ApplicationZoneResponse>)okResult.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(apiConnection.Queries, Is.EqualTo(new List<string>
            {
                ModellingQueries.getAllAppZones,
                OwnerQueries.getOwnersFiltered
            }));
            Assert.That(response.Select(applicationZone => applicationZone.Id), Is.EqualTo(new List<long> { 70, 80 }));
        });
    }

    [Test]
    public async Task GetReturnsOnlyEditableApplicationZonesForEmptyObjectRequest()
    {
        ApplicationZonesApiConnection apiConnection = new()
        {
            Owners = CreateOwners((7, "Application Seven", "APP-7")),
            ZonesByApplicationId = new Dictionary<int, List<ModellingAppZone>>
            {
                [7] = [CreateApplicationZone(7, 70, "AZ-7", "az-7", "10.7.0.1", string.Empty)]
            }
        };
        ClaimsPrincipal modeller = PrincipalWithRolesAndClaims(
            kModellerRole, new Claim("x-hasura-editable-owners", "{7}"));
        ApplicationZonesController controller = CreateController(apiConnection, modeller);

        ActionResult<List<ApplicationZoneResponse>> result = await controller.Get(new GetApplicationZonesRequest());

        OkObjectResult okResult = (OkObjectResult)result.Result!;
        List<ApplicationZoneResponse> response = (List<ApplicationZoneResponse>)okResult.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(apiConnection.ApplicationIds, Is.EqualTo(new List<int> { 7 }));
            Assert.That(response.Select(applicationZone => applicationZone.Id), Is.EqualTo(new List<long> { 70 }));
        });
    }

    [Test]
    public async Task GetReturnsPlaceholderForExistingApplicationWithoutApplicationZone()
    {
        ApplicationZonesApiConnection apiConnection = new()
        {
            Owners = CreateOwners((2, "Application Two", "APP-2"))
        };
        ApplicationZonesController controller = CreateController(apiConnection, PrincipalWithRoles(Roles.Admin));
        GetApplicationZonesRequest request = new() { ApplicationIds = [1, 2] };

        ActionResult<List<ApplicationZoneResponse>> result = await controller.Get(request);

        OkObjectResult okResult = (OkObjectResult)result.Result!;
        List<ApplicationZoneResponse> response = (List<ApplicationZoneResponse>)okResult.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(response, Has.Count.EqualTo(1));
            Assert.That(response[0].ApplicationId, Is.EqualTo(2));
            Assert.That(response[0].ApplicationName, Is.EqualTo("Application Two"));
            Assert.That(response[0].AppIdExternal, Is.EqualTo("APP-2"));
            Assert.That(response[0].Id, Is.Null);
            Assert.That(response[0].Name, Is.Null);
            Assert.That(response[0].IdString, Is.Null);
            Assert.That(response[0].IsDeleted, Is.Null);
            Assert.That(response[0].Addresses, Is.Empty);
        });
    }

    [Test]
    public async Task GetUsesApplicationNameFilterToSelectApplicationWithoutApplicationZone()
    {
        ApplicationZonesApiConnection apiConnection = new()
        {
            Owners = CreateOwners((1, "ownerF_demo", "123"))
        };
        ApplicationZonesController controller = CreateController(apiConnection, PrincipalWithRoles(Roles.Admin));
        GetApplicationZonesRequest request = new()
        {
            Options = new()
            {
                Filter = new() { ApplicationName = "ownerF_demo" }
            }
        };

        ActionResult<List<ApplicationZoneResponse>> result = await controller.Get(request);

        OkObjectResult okResult = (OkObjectResult)result.Result!;
        List<ApplicationZoneResponse> response = (List<ApplicationZoneResponse>)okResult.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(apiConnection.ApplicationIds, Is.EqualTo(new List<int> { 1 }));
            Assert.That(response, Has.Count.EqualTo(1));
            Assert.That(response[0].ApplicationId, Is.EqualTo(1));
            Assert.That(response[0].ApplicationName, Is.EqualTo("ownerF_demo"));
            Assert.That(response[0].AppIdExternal, Is.EqualTo("123"));
            Assert.That(response[0].Id, Is.Null);
            Assert.That(response[0].Addresses, Is.Empty);
        });
    }

    [Test]
    public async Task GetUsesApplicationIdAndExternalIdFiltersToSelectApplicationWithoutApplicationZone()
    {
        ApplicationZonesApiConnection apiConnection = new()
        {
            Owners = CreateOwners((1, "ownerF_demo", "123"))
        };
        ApplicationZonesController controller = CreateController(apiConnection, PrincipalWithRoles(Roles.Admin));
        GetApplicationZonesRequest request = new()
        {
            Options = new()
            {
                Filter = new()
                {
                    ApplicationId = 1,
                    AppIdExternal = "123"
                }
            }
        };

        ActionResult<List<ApplicationZoneResponse>> result = await controller.Get(request);

        OkObjectResult okResult = (OkObjectResult)result.Result!;
        List<ApplicationZoneResponse> response = (List<ApplicationZoneResponse>)okResult.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(response, Has.Count.EqualTo(1));
            Assert.That(response[0].ApplicationId, Is.EqualTo(1));
            Assert.That(response[0].Id, Is.Null);
        });
    }

    [Test]
    public async Task GetSupportsSingleCharacterWildcardsForEveryStringFilter()
    {
        ApplicationZonesApiConnection apiConnection = new()
        {
            Owners = CreateOwners((7, "Application Seven", "APP-7")),
            ZonesByApplicationId = new Dictionary<int, List<ModellingAppZone>>
            {
                [7] = [CreateApplicationZone(7, 70, "AZ-Match", "az-match", "10.7.0.1", string.Empty)]
            }
        };
        ApplicationZonesController controller = CreateController(apiConnection, PrincipalWithRoles(Roles.Auditor));
        GetApplicationZonesRequest request = new()
        {
            Options = new()
            {
                Filter = new()
                {
                    ApplicationName = "Application Seve?",
                    AppIdExternal = "APP-?",
                    Name = "AZ-Matc?",
                    IdString = "az-matc?"
                }
            }
        };

        ActionResult<List<ApplicationZoneResponse>> result = await controller.Get(request);

        OkObjectResult okResult = (OkObjectResult)result.Result!;
        List<ApplicationZoneResponse> response = (List<ApplicationZoneResponse>)okResult.Value!;
        Assert.That(response.Select(applicationZone => applicationZone.Id), Is.EqualTo(new List<long> { 70 }));
    }

    private static ModellingAppZone CreateApplicationZone(
        int applicationId, long id, string name, string idString, string ip, string ipEnd)
    {
        return new ModellingAppZone
        {
            AppId = applicationId,
            Id = id,
            Name = name,
            IdString = idString,
            AppServers =
            [
                new ModellingAppServerWrapper
                {
                    Content = new ModellingAppServer
                    {
                        Id = id + 1,
                        AppId = applicationId,
                        Name = "host-" + applicationId,
                        Ip = ip,
                        IpEnd = ipEnd,
                        ImportSource = "manual",
                        CustomType = 4
                    }
                }
            ]
        };
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

    private static List<FwoOwner> CreateOwners(params (int Id, string Name, string? AppIdExternal)[] owners)
    {
        return owners.Select(owner => new FwoOwner
        {
            Id = owner.Id,
            Name = owner.Name,
            ExtAppId = owner.AppIdExternal
        }).ToList();
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

    private static int GetMaxFilterTextLength()
    {
        FieldInfo maxFilterTextLength = typeof(ApplicationZonesController).GetField(
            "kMaxFilterTextLength", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (int)maxFilterTextLength.GetRawConstantValue()!;
    }

    private sealed class ApplicationZonesApiConnection : SimulatedApiConnection
    {
        public Dictionary<int, List<ModellingAppZone>> ZonesByApplicationId { get; set; } = [];
        public List<ModellingAppZone> AllApplicationZones { get; set; } = [];
        public List<FwoOwner> Owners { get; set; } = [];
        public List<string> Queries { get; } = [];
        public List<int> ApplicationIds { get; } = [];
        public int SetBestRoleCount { get; private set; }

        public override void SetBestRole(ClaimsPrincipal user, List<string> targetRoleList)
        {
            SetBestRoleCount++;
        }

        public override Task<QueryResponseType> SendQueryAsync<QueryResponseType>(
            string query,
            object? variables = null,
            string? operationName = null,
            QueryChunkingOptions? chunkingOptions = null)
        {
            Queries.Add(query);
            if (query == OwnerQueries.getOwnersFiltered)
            {
                return Task.FromResult((QueryResponseType)(object)Owners);
            }
            if (query == ModellingQueries.getAllAppZones)
            {
                return Task.FromResult((QueryResponseType)(object)AllApplicationZones);
            }

            PropertyInfo applicationIdProperty = variables!.GetType().GetProperty("appId")!;
            int applicationId = (int)applicationIdProperty.GetValue(variables)!;
            ApplicationIds.Add(applicationId);
            List<ModellingAppZone> zones = ZonesByApplicationId.GetValueOrDefault(applicationId, []);
            return Task.FromResult((QueryResponseType)(object)zones);
        }
    }
}
