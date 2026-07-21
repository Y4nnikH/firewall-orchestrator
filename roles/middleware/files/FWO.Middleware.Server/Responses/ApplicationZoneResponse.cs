using System.Text.Json.Serialization;

namespace FWO.Middleware.Server.Responses;

/// <summary>
/// Represents an application-zone object and its complete address membership.
/// </summary>
public sealed class ApplicationZoneResponse
{
    /// <summary>
    /// Gets or sets the owning application id.
    /// </summary>
    [JsonPropertyName("applicationId")]
    public int ApplicationId { get; set; }

    /// <summary>
    /// Gets or sets the application-zone database id.
    /// </summary>
    [JsonPropertyName("id")]
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets the application-zone name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the application-zone identifier.
    /// </summary>
    [JsonPropertyName("idString")]
    public string IdString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the application-zone is deleted.
    /// </summary>
    [JsonPropertyName("isDeleted")]
    public bool IsDeleted { get; set; }

    /// <summary>
    /// Gets or sets every address that belongs to the application-zone.
    /// </summary>
    [JsonPropertyName("addresses")]
    public List<ApplicationZoneAddressResponse> Addresses { get; set; } = [];
}

/// <summary>
/// Represents one address in an application-zone object.
/// </summary>
public sealed class ApplicationZoneAddressResponse
{
    /// <summary>
    /// Gets or sets the address database id.
    /// </summary>
    [JsonPropertyName("id")]
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets the address name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the first IP address or network range value.
    /// </summary>
    [JsonPropertyName("ip")]
    public string Ip { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional last IP address of an IP range.
    /// </summary>
    [JsonPropertyName("ipEnd")]
    public string IpEnd { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source that imported the address.
    /// </summary>
    [JsonPropertyName("importSource")]
    public string ImportSource { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the address is deleted.
    /// </summary>
    [JsonPropertyName("isDeleted")]
    public bool IsDeleted { get; set; }

    /// <summary>
    /// Gets or sets the optional custom address type.
    /// </summary>
    [JsonPropertyName("customType")]
    public int? CustomType { get; set; }

    /// <summary>
    /// Gets or sets the owning application id of the address.
    /// </summary>
    [JsonPropertyName("applicationId")]
    public int? ApplicationId { get; set; }
}
