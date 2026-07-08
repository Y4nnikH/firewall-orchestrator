using FWO.Middleware.Server.Controllers;
using FWO.Middleware.Server.OpenApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using NUnit.Framework;
using System.Reflection;

namespace FWO.Test;

/// <summary>
/// Tests endpoint documentation generated for Scalar.
/// </summary>
[TestFixture]
public class OpenApiEndpointDocumentationOperationTransformerTest
{
    /// <summary>
    /// Verifies owner documentation is applied through the already registered OpenAPI transformer.
    /// </summary>
    [Test]
    public async Task TransformAsync_WithOwnerEndpoint_AddsOwnerDescriptionAndResponseDescriptions()
    {
        OpenApiOperation operation = CreateOperation();
        OpenApiOperationTransformerContext context = CreateContext(new ControllerActionDescriptor
        {
            ControllerTypeInfo = typeof(OwnersController).GetTypeInfo(),
            ActionName = nameof(OwnersController.Get)
        });
        OpenApiApiExampleOperationTransformer transformer = CreateTransformer();

        await transformer.TransformAsync(operation, context, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(operation.Description, Does.Contain("Request body examples"));
            Assert.That(operation.Description, Does.Contain("\"showDetails\":true"));
            Assert.That(operation.Description, Does.Contain("Response behavior"));
            Assert.That(operation.Description, Does.Contain(GetMaxFilterTextLength().ToString()));
            Assert.That(operation.RequestBody!.Description, Does.Contain("Optional owner lookup filters"));
            Assert.That(operation.Responses!["200"].Description, Does.Contain("JSON array"));
            Assert.That(operation.Responses["400"].Description, Does.Contain("unsupported property"));
        });
    }

    /// <summary>
    /// Verifies documented owner roles stay aligned with the controller authorization attribute.
    /// </summary>
    [Test]
    public async Task TransformAsync_WithOwnerEndpoint_DocumentsAuthorizedRoles()
    {
        OpenApiOperation operation = CreateOperation();
        OpenApiOperationTransformerContext context = CreateOwnerContext();
        OpenApiApiExampleOperationTransformer transformer = CreateTransformer();
        IEnumerable<string> authorizedRoles = GetOwnerEndpointRoles();

        await transformer.TransformAsync(operation, context, CancellationToken.None);

        Assert.Multiple(() =>
        {
            foreach (string role in authorizedRoles)
            {
                Assert.That(operation.Description, Does.Contain($"`{role}`"));
            }
        });
    }

    /// <summary>
    /// Verifies documented owner response descriptions cover the controller response metadata.
    /// </summary>
    [Test]
    public async Task TransformAsync_WithOwnerEndpoint_DocumentsProducedStatusCodes()
    {
        OpenApiOperation operation = CreateOperation();
        OpenApiOperationTransformerContext context = CreateOwnerContext();
        OpenApiApiExampleOperationTransformer transformer = CreateTransformer();

        await transformer.TransformAsync(operation, context, CancellationToken.None);

        Assert.Multiple(() =>
        {
            foreach (int statusCode in GetOwnerEndpointStatusCodes())
            {
                string key = statusCode.ToString();
                Assert.That(operation.Responses, Does.ContainKey(key));
                Assert.That(operation.Responses![key].Description, Is.Not.Empty);
            }
        });
    }

    /// <summary>
    /// Verifies unrelated endpoints are ignored.
    /// </summary>
    [Test]
    public async Task TransformAsync_WithUnrelatedEndpoint_LeavesDescriptionUntouched()
    {
        OpenApiOperation operation = CreateOperation();
        operation.Description = "Existing description.";
        OpenApiOperationTransformerContext context = CreateContext(new ControllerActionDescriptor(), "api/flow/get-address-objects");
        OpenApiApiExampleOperationTransformer transformer = CreateTransformer();

        await transformer.TransformAsync(operation, context, CancellationToken.None);

        Assert.That(operation.Description, Is.EqualTo("Existing description."));
    }

    /// <summary>
    /// Verifies endpoint documentation providers are discovered without per-endpoint Program.cs registration.
    /// </summary>
    [Test]
    public void AddApiExamples_RegistersEndpointDocumentationProviders()
    {
        ServiceCollection services = new();
        services.AddApiExamples();
        ServiceProvider provider = services.BuildServiceProvider();

        IEnumerable<IOpenApiEndpointDocumentationProvider> providers = provider.GetServices<IOpenApiEndpointDocumentationProvider>();

        Assert.That(providers, Has.One.InstanceOf<OpenApiOwnerDocumentationProvider>());
    }

    private static OpenApiApiExampleOperationTransformer CreateTransformer()
    {
        JsonOptions jsonOptions = new();
        ApiDocumentationJsonOptions.Configure(jsonOptions);

        return new OpenApiApiExampleOperationTransformer(
            new ApiExampleCatalog([], new ApiExampleObjectFactory()),
            Options.Create(jsonOptions),
            [new OpenApiOwnerDocumentationProvider()]);
    }

    private static OpenApiOperationTransformerContext CreateOwnerContext()
    {
        return CreateContext(new ControllerActionDescriptor
        {
            ControllerTypeInfo = typeof(OwnersController).GetTypeInfo(),
            ActionName = nameof(OwnersController.Get)
        });
    }

    private static OpenApiOperation CreateOperation()
    {
        return new OpenApiOperation
        {
            RequestBody = new OpenApiRequestBody(),
            Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse(),
                ["400"] = new OpenApiResponse(),
                ["401"] = new OpenApiResponse(),
                ["403"] = new OpenApiResponse(),
                ["500"] = new OpenApiResponse()
            }
        };
    }

    private static OpenApiOperationTransformerContext CreateContext(ControllerActionDescriptor actionDescriptor, string? relativePath = null)
    {
        return new OpenApiOperationTransformerContext
        {
            DocumentName = "v1",
            ApplicationServices = new ServiceCollection().BuildServiceProvider(),
            Description = new ApiDescription
            {
                ActionDescriptor = actionDescriptor,
                RelativePath = relativePath
            }
        };
    }

    private static IEnumerable<string> GetOwnerEndpointRoles()
    {
        MethodInfo getMethod = typeof(OwnersController).GetMethod(nameof(OwnersController.Get))!;
        AuthorizeAttribute authorize = getMethod.GetCustomAttribute<AuthorizeAttribute>()!;
        return authorize.Roles!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static IEnumerable<int> GetOwnerEndpointStatusCodes()
    {
        MethodInfo getMethod = typeof(OwnersController).GetMethod(nameof(OwnersController.Get))!;
        return getMethod.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode);
    }

    private static int GetMaxFilterTextLength()
    {
        FieldInfo maxFilterTextLength = typeof(OwnersController).GetField("kMaxFilterTextLength", BindingFlags.NonPublic | BindingFlags.Static)!
            ?? throw new InvalidOperationException("Owner filter length constant is missing.");
        return (int)maxFilterTextLength.GetRawConstantValue()!;
    }
}
