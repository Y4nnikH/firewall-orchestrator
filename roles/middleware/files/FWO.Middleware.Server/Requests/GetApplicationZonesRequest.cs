using System.Text.Json.Serialization;

namespace FWO.Middleware.Server.Requests;

/// <summary>
/// Represents a request for application-zone objects.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class GetApplicationZonesRequest
{
    /// <summary>
    /// Gets or sets the optional list of positive application ids to query. When omitted, null, or empty,
    /// application zones for every application visible to the caller are returned.
    /// </summary>
    [JsonPropertyName("applicationIds")]
    public List<int>? ApplicationIds { get; set; }

    /// <summary>
    /// Gets or sets the optional response options. When omitted, this defaults to an empty object.
    /// </summary>
    [JsonPropertyName("options")]
    public GetApplicationZonesOptions? Options { get; set; } = new();
}

/// <summary>
/// Represents optional application-zone response options.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class GetApplicationZonesOptions
{
    /// <summary>
    /// Gets or sets the optional response filter. Null or omitted filter fields do not restrict the result.
    /// </summary>
    [JsonPropertyName("filter")]
    public ApplicationZoneFilter? Filter { get; set; }
}

/// <summary>
/// Represents nullable filters for every top-level application-zone response field.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ApplicationZoneFilter
{
    /// <summary>
    /// Gets or sets the optional application id filter.
    /// </summary>
    [JsonPropertyName("applicationId")]
    public int? ApplicationId { get; set; }

    /// <summary>
    /// Gets or sets the optional exact, case-insensitive application name filter.
    /// </summary>
    [JsonPropertyName("applicationName")]
    public string? ApplicationName { get; set; }

    /// <summary>
    /// Gets or sets the optional exact, case-insensitive external application-id filter.
    /// </summary>
    [JsonPropertyName("appIdExternal")]
    public string? AppIdExternal { get; set; }

    /// <summary>
    /// Gets or sets the optional application-zone database id filter.
    /// </summary>
    [JsonPropertyName("id")]
    public long? Id { get; set; }

    /// <summary>
    /// Gets or sets the optional exact, case-insensitive application-zone name filter.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the optional exact, case-insensitive application-zone identifier filter.
    /// </summary>
    [JsonPropertyName("idString")]
    public string? IdString { get; set; }

    /// <summary>
    /// Gets or sets the optional deleted-state filter.
    /// </summary>
    [JsonPropertyName("isDeleted")]
    public bool? IsDeleted { get; set; }
}
