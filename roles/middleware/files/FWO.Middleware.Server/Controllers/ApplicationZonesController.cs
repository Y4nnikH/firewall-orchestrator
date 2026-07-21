using FWO.Api.Client;
using FWO.Api.Client.Queries;
using FWO.Basics;
using FWO.Data.Modelling;
using FWO.Logging;
using FWO.Middleware.Server.Requests;
using FWO.Middleware.Server.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FWO.Middleware.Server.Controllers;

/// <summary>
/// Provides read-only application-zone lookup endpoints.
/// </summary>
[Authorize]
[ApiController]
[Route("api/modelling")]
public class ApplicationZonesController(ApiConnection apiConnection) : ControllerBase
{
    internal const int kMaxFilterTextLength = 256;

    /// <summary>
    /// Returns complete application-zone objects, including all addresses, for the requested applications.
    /// </summary>
    /// <remarks>
    /// Requires the <c>admin</c>, <c>auditor</c>, or <c>modeller</c> role. A caller with only the modeller role
    /// receives application zones only for applications in the <c>x-hasura-editable-owners</c> JWT claim.
    /// The <c>applicationIds</c> root key is required. The <c>options</c> root key defaults to <c>{}</c> when omitted.
    /// Every field in <c>options.filter</c> is nullable; an omitted or null field does not restrict the response.
    /// </remarks>
    [HttpPost("getApplicationZones")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(List<ApplicationZoneResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize(Roles = $"{Roles.Admin}, {Roles.Auditor}, {Roles.Modeller}")]
    public async Task<ActionResult<List<ApplicationZoneResponse>>> Get([FromBody] GetApplicationZonesRequest? request)
    {
        Dictionary<string, string[]> validationErrors = ValidateRequest(request);
        if (validationErrors.Count > 0)
        {
            return BadRequest(new ValidationProblemDetails(validationErrors)
            {
                Status = StatusCodes.Status400BadRequest
            });
        }

        try
        {
            GetApplicationZonesRequest effectiveRequest = request!;
            List<int> applicationIds = GetAccessibleApplicationIds(effectiveRequest.ApplicationIds!);
            List<ApplicationZoneResponse> applicationZones = await GetApplicationZonesAsync(applicationIds);
            return Ok(ApplyFilter(applicationZones, effectiveRequest.Options!.Filter));
        }
        catch (Exception exception)
        {
            Log.WriteError("Get Application Zones", "Error while fetching application zones.", exception);
            return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error");
        }
    }

    /// <summary>
    /// Validates the request and returns all detected field errors.
    /// </summary>
    internal static Dictionary<string, string[]> ValidateRequest(GetApplicationZonesRequest? request)
    {
        Dictionary<string, string[]> errors = [];
        if (request is null)
        {
            AddError(errors, "request", "A request body is required.");
            return errors;
        }

        ValidateApplicationIds(request.ApplicationIds, errors);
        if (request.Options is null)
        {
            AddError(errors, "options", "options must be an object when supplied.");
            return errors;
        }

        ValidateFilter(request.Options.Filter, errors);
        return errors;
    }

    /// <summary>
    /// Maps one modelling application-zone object to the public response shape.
    /// </summary>
    internal static ApplicationZoneResponse ToResponse(ModellingAppZone applicationZone)
    {
        return new ApplicationZoneResponse
        {
            ApplicationId = applicationZone.AppId ?? 0,
            Id = applicationZone.Id,
            Name = applicationZone.Name,
            IdString = applicationZone.IdString,
            IsDeleted = applicationZone.IsDeleted,
            Addresses = applicationZone.AppServers.Select(appServer => new ApplicationZoneAddressResponse
            {
                Id = appServer.Content.Id,
                Name = appServer.Content.Name,
                Ip = appServer.Content.Ip,
                IpEnd = appServer.Content.IpEnd,
                ImportSource = appServer.Content.ImportSource,
                IsDeleted = appServer.Content.IsDeleted,
                CustomType = appServer.Content.CustomType,
                ApplicationId = appServer.Content.AppId
            }).ToList()
        };
    }

    private static void ValidateApplicationIds(List<int>? applicationIds, Dictionary<string, string[]> errors)
    {
        if (applicationIds is not { Count: > 0 })
        {
            AddError(errors, "applicationIds", "applicationIds is required and must contain at least one positive integer.");
            return;
        }

        for (int index = 0; index < applicationIds.Count; index++)
        {
            if (applicationIds[index] <= 0)
            {
                AddError(errors, $"applicationIds[{index}]", "applicationIds entries must be positive integers.");
            }
        }
    }

    private static void ValidateFilter(ApplicationZoneFilter? filter, Dictionary<string, string[]> errors)
    {
        if (filter is null)
        {
            return;
        }

        ValidatePositiveValue(filter.ApplicationId, "options.filter.applicationId", errors);
        ValidatePositiveValue(filter.Id, "options.filter.id", errors);
        ValidateFilterText(filter.Name, "options.filter.name", errors);
        ValidateFilterText(filter.IdString, "options.filter.idString", errors);
    }

    private static void ValidatePositiveValue(long? value, string fieldName, Dictionary<string, string[]> errors)
    {
        if (value is <= 0)
        {
            AddError(errors, fieldName, $"{fieldName} must be a positive integer when supplied.");
        }
    }

    private static void ValidateFilterText(string? value, string fieldName, Dictionary<string, string[]> errors)
    {
        if (value is null)
        {
            return;
        }
        if (value.Length > kMaxFilterTextLength)
        {
            AddError(errors, fieldName, $"{fieldName} must not exceed {kMaxFilterTextLength} characters.");
        }
        if (value.Any(char.IsControl))
        {
            AddError(errors, fieldName, $"{fieldName} must not contain control characters.");
        }
    }

    private List<int> GetAccessibleApplicationIds(List<int> applicationIds)
    {
        if (!ShouldRestrictToEditableApplications(User))
        {
            return applicationIds.Distinct().ToList();
        }

        HashSet<int> editableApplicationIds = JwtClaimParser.ExtractIntClaimValues(
            User.Claims, "x-hasura-editable-owners").ToHashSet();
        return applicationIds.Where(editableApplicationIds.Contains).Distinct().ToList();
    }

    private async Task<List<ApplicationZoneResponse>> GetApplicationZonesAsync(List<int> applicationIds)
    {
        List<ApplicationZoneResponse> applicationZones = [];
        foreach (int applicationId in applicationIds)
        {
            List<ModellingAppZone> zones = await apiConnection.SendQueryAsync<List<ModellingAppZone>>(
                ModellingQueries.getAppZonesByAppId, new { appId = applicationId }) ?? [];
            applicationZones.AddRange(zones.Select(ToResponse));
        }
        return applicationZones;
    }

    private static List<ApplicationZoneResponse> ApplyFilter(
        List<ApplicationZoneResponse> applicationZones, ApplicationZoneFilter? filter)
    {
        if (filter is null)
        {
            return applicationZones;
        }

        return applicationZones.Where(applicationZone =>
            (filter.ApplicationId is null || applicationZone.ApplicationId == filter.ApplicationId) &&
            (filter.Id is null || applicationZone.Id == filter.Id) &&
            (filter.Name is null || string.Equals(applicationZone.Name, filter.Name, StringComparison.OrdinalIgnoreCase)) &&
            (filter.IdString is null || string.Equals(applicationZone.IdString, filter.IdString, StringComparison.OrdinalIgnoreCase)) &&
            (filter.IsDeleted is null || applicationZone.IsDeleted == filter.IsDeleted)).ToList();
    }

    private static void AddError(Dictionary<string, string[]> errors, string fieldName, string error)
    {
        string[] errorValues = new string[1];
        errorValues[0] = error;
        errors[fieldName] = errorValues;
    }

    private static bool ShouldRestrictToEditableApplications(System.Security.Claims.ClaimsPrincipal user)
    {
        return user.IsInRole(Roles.Modeller) && !user.IsInRole(Roles.Admin) && !user.IsInRole(Roles.Auditor);
    }
}
