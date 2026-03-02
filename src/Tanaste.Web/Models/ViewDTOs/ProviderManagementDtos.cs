using System.Text.Json.Serialization;

namespace Tanaste.Web.Models.ViewDTOs;

/// <summary>
/// Result of testing a provider's connectivity.
/// Maps from <c>POST /settings/providers/{name}/test</c>.
/// </summary>
public sealed record ProviderTestResultDto(
    [property: JsonPropertyName("success")]         bool   Success,
    [property: JsonPropertyName("responseTimeMs")]  int    ResponseTimeMs,
    [property: JsonPropertyName("sampleFields")]    List<string> SampleFields,
    [property: JsonPropertyName("message")]         string Message);

/// <summary>
/// Result of fetching sample claims from a provider.
/// Maps from <c>POST /settings/providers/{name}/sample</c>.
/// </summary>
public sealed record ProviderSampleResultDto(
    [property: JsonPropertyName("providerName")]  string ProviderName,
    [property: JsonPropertyName("claims")]        List<ProviderSampleClaimDto> Claims);

/// <summary>A single sample claim returned from a provider sample fetch.</summary>
public sealed record ProviderSampleClaimDto(
    [property: JsonPropertyName("key")]        string Key,
    [property: JsonPropertyName("value")]      string Value,
    [property: JsonPropertyName("confidence")] double Confidence);

/// <summary>
/// Request body for saving a provider's full configuration.
/// Maps to <c>PUT /settings/providers/{name}/config</c>.
/// </summary>
public sealed class ProviderConfigUpdateDto
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("weight")]
    public double Weight { get; set; } = 1.0;

    [JsonPropertyName("field_weights")]
    public Dictionary<string, double> FieldWeights { get; set; } = new();

    [JsonPropertyName("capability_tags")]
    public List<string> CapabilityTags { get; set; } = [];

    [JsonPropertyName("endpoints")]
    public Dictionary<string, string> Endpoints { get; set; } = new();

    [JsonPropertyName("throttle_ms")]
    public int ThrottleMs { get; set; } = 500;

    [JsonPropertyName("max_concurrency")]
    public int MaxConcurrency { get; set; } = 1;

    [JsonPropertyName("field_mappings")]
    public List<FieldMappingDto>? FieldMappings { get; set; }
}

/// <summary>Field mapping entry for config-driven provider editing.</summary>
public sealed class FieldMappingDto
{
    [JsonPropertyName("claim_key")]
    public string ClaimKey { get; set; } = string.Empty;

    [JsonPropertyName("json_path")]
    public string JsonPath { get; set; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; } = 0.5;

    [JsonPropertyName("transform")]
    public string? Transform { get; set; }

    [JsonPropertyName("transform_args")]
    public string? TransformArgs { get; set; }
}
