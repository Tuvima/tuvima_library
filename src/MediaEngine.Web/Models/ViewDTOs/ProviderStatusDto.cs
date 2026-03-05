using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

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
    [property: JsonPropertyName("available_fields")]  List<string>? AvailableFields                        = null,
    [property: JsonPropertyName("media_types")]       List<string>? MediaTypes                              = null,
    [property: JsonPropertyName("requires_api_key")]  bool RequiresApiKey                                  = false,
    [property: JsonPropertyName("has_api_key")]       bool HasApiKey                                       = false,
    [property: JsonPropertyName("api_key_delivery")]  string? ApiKeyDelivery                               = null,
    [property: JsonPropertyName("api_key_param_name")]string? ApiKeyParamName                              = null,
    [property: JsonPropertyName("timeout_seconds")]   int TimeoutSeconds                                   = 10,
    [property: JsonPropertyName("custom_icon_name")]  string? CustomIconName                               = null);
