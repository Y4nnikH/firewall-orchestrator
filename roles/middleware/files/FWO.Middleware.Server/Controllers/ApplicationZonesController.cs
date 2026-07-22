using FWO.Api.Client;
using FWO.Api.Client.Queries;
using FWO.Basics;
using FWO.Data;
using FWO.Data.Modelling;
using FWO.Logging;
using FWO.Middleware.Server.Requests;
using FWO.Middleware.Server.Responses;
using FWO.Middleware.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

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
    internal const int kMaxLimit = 1000;

    /// <summary>
    /// Returns complete application-zone objects, including all addresses, for visible applications.
    /// </summary>
    /// <remarks>
    /// Requires the <c>admin</c>, <c>auditor</c>, or <c>modeller</c> role. A caller with only the modeller role
    /// receives application zones only for applications in the <c>x-hasura-editable-owners</c> JWT claim.
    /// The <c>options</c> root key defaults to <c>{}</c> when omitted.
    /// Every field in <c>options.filter</c> is nullable; an omitted or null field does not restrict the response.
    /// String filters are case-insensitive and accept <c>*</c> for any character sequence and <c>?</c> for a single
    /// character. Plain text without wildcards is matched as a contains search, matching the owner endpoint.
    /// Deleted application zones and member addresses are excluded by default. Zone-specific filters exclude the
    /// placeholder returned for applications that have no application zone.
    /// Applications with an inactive lifecycle state are excluded unless <c>options.showOnlyActiveState</c> is set to
    /// <c>false</c>. Applications are ordered by name, so <c>options.limit</c> and <c>options.offset</c> page the
    /// result deterministically; without a limit every matching application is returned.
    /// </remarks>
    [HttpPost("getApplicationZones")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(List<ApplicationZoneResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize(Roles = $"{Roles.Admin}, {Roles.Auditor}, {Roles.Modeller}")]
    public async Task<ActionResult<List<ApplicationZoneResponse>>> Get(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] GetApplicationZonesRequest? request)
    {
        GetApplicationZonesRequest effectiveRequest = request ?? new GetApplicationZonesRequest();
        Dictionary<string, string[]> validationErrors = ValidateRequest(effectiveRequest);
        if (validationErrors.Count > 0)
        {
            return BadRequest(new ValidationProblemDetails(validationErrors)
            {
                Status = StatusCodes.Status400BadRequest
            });
        }

        try
        {
            GetApplicationZonesOptions options = effectiveRequest.Options!;
            List<FwoOwner> applications = await GetApplicationsAsync(options);
            return Ok(await GetApplicationZonesAsync(applications, options.Filter));
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
    internal static Dictionary<string, string[]> ValidateRequest(GetApplicationZonesRequest request)
    {
        Dictionary<string, string[]> errors = [];
        if (request.Options is null)
        {
            AddError(errors, "options", "options must be an object when supplied.");
            return errors;
        }

        ValidateFilter(request.Options.Filter, errors);
        ValidateLimit(request.Options.Limit, errors);
        ValidateOffset(request.Options.Offset, errors);
        return errors;
    }

    /// <summary>
    /// Maps one modelling application-zone object and its owning application to the public response shape.
    /// </summary>
    internal static ApplicationZoneResponse ToResponse(ModellingAppZone applicationZone, FwoOwner application)
    {
        return new ApplicationZoneResponse
        {
            ApplicationId = application.Id,
            ApplicationName = application.Name,
            AppIdExternal = application.ExtAppId,
            Id = applicationZone.Id,
            Name = applicationZone.Name,
            IdString = applicationZone.IdString,
            IsDeleted = applicationZone.IsDeleted,
            Addresses = applicationZone.AppServers.Select(appServer => new ApplicationZoneAddressResponse
            {
                Id = appServer.Content.Id,
                Name = appServer.Content.Name,
                Ip = IpOperations.ToCompactNotation(appServer.Content.Ip, appServer.Content.IpEnd),
                IpStart = appServer.Content.Ip,
                IpEnd = appServer.Content.IpEnd,
                ImportSource = appServer.Content.ImportSource,
                IsDeleted = appServer.Content.IsDeleted,
                CustomType = appServer.Content.CustomType,
                ApplicationId = appServer.Content.AppId
            }).ToList()
        };
    }

    private static void ValidateFilter(ApplicationZoneFilter? filter, Dictionary<string, string[]> errors)
    {
        if (filter is null)
        {
            return;
        }

        ValidatePositiveValue(filter.ApplicationId, "options.filter.applicationId", errors);
        ValidateFilterText(filter.ApplicationName, "options.filter.applicationName", errors);
        ValidateFilterText(filter.AppIdExternal, "options.filter.appIdExternal", errors);
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

    private static void ValidateLimit(int? limit, Dictionary<string, string[]> errors)
    {
        if (limit is not null && (limit < 1 || limit > kMaxLimit))
        {
            AddError(errors, "options.limit", $"options.limit must be between 1 and {kMaxLimit} when supplied.");
        }
    }

    private static void ValidateOffset(int? offset, Dictionary<string, string[]> errors)
    {
        if (offset is < 0)
        {
            AddError(errors, "options.offset", "options.offset must not be negative when supplied.");
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

    private async Task<List<FwoOwner>> GetApplicationsAsync(GetApplicationZonesOptions options)
    {
        return await apiConnection.SendQueryAsync<List<FwoOwner>>(
            OwnerQueries.getOwnersFiltered,
            ApplicationZoneQueryBuilder.BuildApplicationVariables(options, User)) ?? [];
    }

    private async Task<List<ApplicationZoneResponse>> GetApplicationZonesAsync(
        List<FwoOwner> applications, ApplicationZoneFilter? filter)
    {
        // Deleted application zones are never returned, so an explicit request for them stays empty.
        if (applications.Count == 0 || filter?.IsDeleted == true)
        {
            return [];
        }

        List<int> applicationIds = applications.Select(application => application.Id).ToList();
        List<ModellingAppZone> zones = await apiConnection.SendQueryAsync<List<ModellingAppZone>>(
            ModellingQueries.getAppZones,
            ApplicationZoneQueryBuilder.BuildApplicationZoneVariables(applicationIds, filter)) ?? [];
        return BuildResponses(applications, zones, ApplicationZoneQueryBuilder.HasZoneFilter(filter));
    }

    private static List<ApplicationZoneResponse> BuildResponses(
        List<FwoOwner> applications, List<ModellingAppZone> zones, bool hasZoneFilter)
    {
        Dictionary<int, List<ModellingAppZone>> zonesByApplicationId = zones
            .Where(zone => zone.AppId is not null)
            .GroupBy(zone => zone.AppId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());
        List<ApplicationZoneResponse> responses = [];

        foreach (FwoOwner application in applications)
        {
            if (zonesByApplicationId.TryGetValue(application.Id, out List<ModellingAppZone>? applicationZones))
            {
                responses.AddRange(applicationZones.Select(zone => ToResponse(zone, application)));
            }
            else if (!hasZoneFilter)
            {
                responses.Add(CreatePlaceholder(application));
            }
        }
        return responses;
    }

    private static ApplicationZoneResponse CreatePlaceholder(FwoOwner application)
    {
        return new ApplicationZoneResponse
        {
            ApplicationId = application.Id,
            ApplicationName = application.Name,
            AppIdExternal = application.ExtAppId
        };
    }

    private static void AddError(Dictionary<string, string[]> errors, string fieldName, string error)
    {
        string[] errorValues = new string[1];
        errorValues[0] = error;
        errors[fieldName] = errorValues;
    }
}
