using System.Text.Json.Serialization;

namespace Tanaste.Web.Models.ViewDTOs;

/// <summary>
/// Status of one external metadata provider, returned by
/// <c>GET /settings/providers</c>.
/// </summary>
public sealed record ProviderStatusDto(
    [property: JsonPropertyName("name")]              string Name,
    [property: JsonPropertyName("display_name")]      string DisplayName,
    [property: JsonPropertyName("enabled")]           bool   Enabled,
    [property: JsonPropertyName("is_zero_key")]       bool   IsZeroKey,
    [property: JsonPropertyName("is_reachable")]      bool   IsReachable,
    [property: JsonPropertyName("domain")]            string Domain                                        = "",
    [property: JsonPropertyName("capability_tags")]   List<string>? CapabilityTags                         = null,
    [property: JsonPropertyName("default_weight")]    double DefaultWeight                                 = 1.0,
    [property: JsonPropertyName("field_weights")]     Dictionary<string, double>? FieldWeights             = null,
    [property: JsonPropertyName("hydration_stages")]  List<int>? HydrationStages                           = null,
    [property: JsonPropertyName("endpoints")]         Dictionary<string, string>? Endpoints                = null,
    [property: JsonPropertyName("field_mappings")]    List<FieldMappingDto>? FieldMappings                 = null,
    [property: JsonPropertyName("throttle_ms")]       int ThrottleMs                                       = 0,
    [property: JsonPropertyName("max_concurrency")]   int MaxConcurrency                                   = 1,
    [property: JsonPropertyName("available_fields")]  List<string>? AvailableFields                        = null);
