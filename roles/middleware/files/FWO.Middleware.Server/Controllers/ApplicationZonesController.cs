using FWO.Api.Client;
using FWO.Api.Client.Queries;
using FWO.Basics;
using FWO.Data;
using FWO.Data.Modelling;
using FWO.Logging;
using FWO.Middleware.Server.Requests;
using FWO.Middleware.Server.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Text.RegularExpressions;

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
    private const int kFilterPatternTimeoutMilliseconds = 100;

    /// <summary>
    /// Returns complete application-zone objects, including all addresses, for visible applications.
    /// </summary>
    /// <remarks>
    /// Requires the <c>admin</c>, <c>auditor</c>, or <c>modeller</c> role. A caller with only the modeller role
    /// receives application zones only for applications in the <c>x-hasura-editable-owners</c> JWT claim.
    /// The <c>options</c> root key defaults to <c>{}</c> when omitted.
    /// Every field in <c>options.filter</c> is nullable; an omitted or null field does not restrict the response.
    /// Deleted application zones and member addresses are excluded by default. Zone-specific filters exclude the
    /// placeholder returned for applications that have no application zone.
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
            List<int>? accessibleApplicationIds = GetAccessibleApplicationIds();
            List<FwoOwner> filteredApplications = await GetFilteredApplicationsAsync(
                accessibleApplicationIds, effectiveRequest.Options!.Filter);
            List<ApplicationZoneResponse> applicationZones = await GetApplicationZonesAsync(filteredApplications);
            return Ok(ApplyZoneFilter(applicationZones, effectiveRequest.Options!.Filter));
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

    private List<int>? GetAccessibleApplicationIds()
    {
        if (ShouldRestrictToEditableApplications(User))
        {
            return JwtClaimParser.ExtractIntClaimValues(User.Claims, "x-hasura-editable-owners").ToList();
        }

        return null;
    }

    private async Task<List<ApplicationZoneResponse>> GetApplicationZonesAsync(List<FwoOwner> applications)
    {
        if (applications.Count == 0)
        {
            return [];
        }

        List<int> applicationIds = applications.Select(application => application.Id).ToList();
        HashSet<int> applicationIdSet = applicationIds.ToHashSet();
        List<ModellingAppZone> zones = await apiConnection.SendQueryAsync<List<ModellingAppZone>>(
            ModellingQueries.geAppZones, new { applicationIds }) ?? [];
        List<ApplicationZoneResponse> applicationZones = zones
            .Where(zone => zone.AppId is not null && applicationIdSet.Contains(zone.AppId.Value))
            .Select(ToResponse)
            .ToList();
        return AddApplicationZoneDetails(applicationIds, applicationZones, applications);
    }

    private async Task<List<FwoOwner>> GetFilteredApplicationsAsync(
        List<int>? applicationIds, ApplicationZoneFilter? filter)
    {
        List<FwoOwner> applications = await GetExistingApplicationsAsync(applicationIds);
        return applications.Where(application =>
            (filter?.ApplicationId is null || application.Id == filter.ApplicationId) &&
            MatchesTextFilter(application.Name, filter?.ApplicationName) &&
            MatchesTextFilter(application.ExtAppId, filter?.AppIdExternal)).ToList();
    }

    private async Task<List<FwoOwner>> GetExistingApplicationsAsync(List<int>? applicationIds)
    {
        Dictionary<string, object> where = new()
        {
            ["is_default"] = new Dictionary<string, object> { ["_eq"] = false }
        };
        if (applicationIds is not null)
        {
            where["id"] = new Dictionary<string, object> { ["_in"] = applicationIds };
        }
        return await apiConnection.SendQueryAsync<List<FwoOwner>>(
            OwnerQueries.getOwnersFiltered, new { where }) ?? [];
    }

    private static List<ApplicationZoneResponse> AddApplicationZoneDetails(
        List<int> applicationIds,
        List<ApplicationZoneResponse> applicationZones,
        List<FwoOwner> applications)
    {
        Dictionary<int, FwoOwner> applicationsById = applications.ToDictionary(application => application.Id);
        Dictionary<int, List<ApplicationZoneResponse>> zonesByApplicationId = applicationZones
            .GroupBy(applicationZone => applicationZone.ApplicationId)
            .ToDictionary(group => group.Key, group => group.ToList());
        List<ApplicationZoneResponse> responses = [];

        foreach (int applicationId in applicationIds.Distinct())
        {
            if (!applicationsById.TryGetValue(applicationId, out FwoOwner? application))
            {
                continue;
            }

            if (zonesByApplicationId.TryGetValue(applicationId, out List<ApplicationZoneResponse>? zones))
            {
                foreach (ApplicationZoneResponse zone in zones)
                {
                    zone.ApplicationName = application.Name;
                    zone.AppIdExternal = application.ExtAppId;
                }
                responses.AddRange(zones);
            }
            else
            {
                responses.Add(new ApplicationZoneResponse
                {
                    ApplicationId = applicationId,
                    ApplicationName = application.Name,
                    AppIdExternal = application.ExtAppId
                });
            }
        }
        return responses;
    }

    private static List<ApplicationZoneResponse> ApplyZoneFilter(
        List<ApplicationZoneResponse> applicationZones, ApplicationZoneFilter? filter)
    {
        if (filter is null)
        {
            return applicationZones;
        }

        return applicationZones.Where(applicationZone =>
            (filter.Id is null || applicationZone.Id == filter.Id) &&
            MatchesTextFilter(applicationZone.Name, filter.Name) &&
            MatchesTextFilter(applicationZone.IdString, filter.IdString) &&
            (filter.IsDeleted is null || applicationZone.IsDeleted == filter.IsDeleted)).ToList();
    }

    private static bool MatchesTextFilter(string? value, string? filter)
    {
        if (filter is null)
        {
            return true;
        }
        if (value is null)
        {
            return false;
        }

        string pattern = "^" + Regex.Escape(filter)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        try
        {
            return Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(kFilterPatternTimeoutMilliseconds));
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
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
