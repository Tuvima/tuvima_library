using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.Settings;

public sealed class ProviderCatalogueDto
{
    [JsonPropertyName("providerId")]
    public string ProviderId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("domain")]
    public string Domain { get; set; } = string.Empty;

    [JsonPropertyName("mediaTypes")]
    public List<string> MediaTypes { get; set; } = [];

    [JsonPropertyName("accentColor")]
    public string AccentColor { get; set; } = "#90A4AE";

    [JsonPropertyName("materialIcon")]
    public string MaterialIcon { get; set; } = "Cloud";

    [JsonPropertyName("externalUrlTemplate")]
    public string? ExternalUrlTemplate { get; set; }

    [JsonPropertyName("externalLinks")]
    public Dictionary<string, ProviderExternalLinkDto> ExternalLinks { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("category")]
    public string Category { get; set; } = "Open";

    [JsonPropertyName("requiresKey")]
    public bool RequiresKey { get; set; }

    [JsonPropertyName("authType")]
    public string AuthType { get; set; } = "none";

    [JsonPropertyName("searchChips")]
    public Dictionary<string, List<string>> SearchChips { get; set; } = [];

    [JsonPropertyName("rankingChips")]
    public Dictionary<string, List<string>> RankingChips { get; set; } = [];

    [JsonPropertyName("iconPath")]
    public string? IconPath { get; set; }

    [JsonPropertyName("hydrationStages")]
    public List<int> HydrationStages { get; set; } = [];

    [JsonPropertyName("languageStrategy")]
    public string LanguageStrategy { get; set; } = "source";
}

public sealed class ProviderExternalLinkDto
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = "View source";

    [JsonPropertyName("urlTemplate")]
    public string UrlTemplate { get; set; } = string.Empty;

    [JsonPropertyName("tooltip")]
    public string? Tooltip { get; set; }
}

public sealed record ProviderStatusDto(
    [property: JsonPropertyName("name")] string Name = "",
    [property: JsonPropertyName("display_name")] string DisplayName = "",
    [property: JsonPropertyName("enabled")] bool Enabled = false,
    [property: JsonPropertyName("is_zero_key")] bool IsZeroKey = false,
    [property: JsonPropertyName("is_reachable")] bool IsReachable = false,
    [property: JsonPropertyName("domain")] string Domain = "",
    [property: JsonPropertyName("capability_tags")] List<string>? CapabilityTags = null,
    [property: JsonPropertyName("default_weight")] double DefaultWeight = 1.0,
    [property: JsonPropertyName("field_weights")] Dictionary<string, double>? FieldWeights = null,
    [property: JsonPropertyName("hydration_stages")] List<int>? HydrationStages = null,
    [property: JsonPropertyName("endpoints")] Dictionary<string, string>? Endpoints = null,
    [property: JsonPropertyName("field_mappings")] List<FieldMappingDto>? FieldMappings = null,
    [property: JsonPropertyName("throttle_ms")] int ThrottleMs = 0,
    [property: JsonPropertyName("max_concurrency")] int MaxConcurrency = 1,
    [property: JsonPropertyName("language_strategy")] string? LanguageStrategy = null,
    [property: JsonPropertyName("available_fields")] List<string>? AvailableFields = null,
    [property: JsonPropertyName("media_types")] List<string>? MediaTypes = null,
    [property: JsonPropertyName("requires_api_key")] bool RequiresApiKey = false,
    [property: JsonPropertyName("has_api_key")] bool HasApiKey = false,
    [property: JsonPropertyName("api_key_delivery")] string? ApiKeyDelivery = null,
    [property: JsonPropertyName("api_key_param_name")] string? ApiKeyParamName = null,
    [property: JsonPropertyName("timeout_seconds")] int TimeoutSeconds = 10,
    [property: JsonPropertyName("custom_icon_name")] string? CustomIconName = null,
    [property: JsonPropertyName("health_status")] string? HealthStatus = null,
    [property: JsonPropertyName("consecutive_failures")] int ConsecutiveFailures = 0,
    [property: JsonPropertyName("last_success_at")] string? LastSuccessAt = null,
    [property: JsonPropertyName("last_failure_at")] string? LastFailureAt = null,
    [property: JsonPropertyName("last_failure_reason")] string? LastFailureReason = null,
    [property: JsonPropertyName("down_since")] string? DownSince = null);

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

public sealed class ProviderTestResultDto
{
    public ProviderTestResultDto()
    {
    }

    public ProviderTestResultDto(bool success, int responseTimeMs, List<string> sampleFields, string message)
    {
        Success = success;
        ResponseTimeMs = responseTimeMs;
        SampleFields = sampleFields;
        Message = message;
    }

    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("response_time_ms")]
    public int ResponseTimeMs { get; init; }

    [JsonPropertyName("sample_fields")]
    public List<string> SampleFields { get; init; } = [];

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

public sealed class ProviderSampleResultDto
{
    public ProviderSampleResultDto()
    {
    }

    public ProviderSampleResultDto(
        string providerName,
        List<ProviderSampleClaimDto> claims,
        string? message = null)
    {
        ProviderName = providerName;
        Claims = claims;
        Message = message;
    }

    [JsonPropertyName("provider_name")]
    public string ProviderName { get; init; } = string.Empty;

    [JsonPropertyName("claims")]
    public List<ProviderSampleClaimDto> Claims { get; init; } = [];

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

public sealed class ProviderSampleClaimDto
{
    public ProviderSampleClaimDto()
    {
    }

    public ProviderSampleClaimDto(string key, string value, double confidence)
    {
        Key = key;
        Value = value;
        Confidence = confidence;
    }

    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; init; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }
}

public sealed class UpdateProviderRequest
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }
}

public sealed class ProviderSampleRequest
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("author")]
    public string? Author { get; init; }

    [JsonPropertyName("isbn")]
    public string? Isbn { get; init; }

    [JsonPropertyName("asin")]
    public string? Asin { get; init; }

    [JsonPropertyName("media_type")]
    public string? MediaType { get; init; }
}

public sealed class ProviderConfigUpdateDto
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; } = true;

    [JsonPropertyName("weight")]
    public double? Weight { get; set; } = 1.0;

    [JsonPropertyName("field_weights")]
    public Dictionary<string, double>? FieldWeights { get; set; } = new();

    [JsonPropertyName("capability_tags")]
    public List<string>? CapabilityTags { get; set; } = [];

    [JsonPropertyName("endpoints")]
    public Dictionary<string, string>? Endpoints { get; set; } = new();

    [JsonPropertyName("throttle_ms")]
    public int? ThrottleMs { get; set; } = 500;

    [JsonPropertyName("max_concurrency")]
    public int? MaxConcurrency { get; set; } = 1;

    [JsonPropertyName("language_strategy")]
    public string? LanguageStrategy { get; set; }

    [JsonPropertyName("field_mappings")]
    public List<FieldMappingDto>? FieldMappings { get; set; }

    [JsonPropertyName("timeout_seconds")]
    public int? TimeoutSeconds { get; set; }

    [JsonPropertyName("api_key")]
    public string? ApiKey { get; set; }

    [JsonPropertyName("custom_icon_name")]
    public string? CustomIconName { get; set; }
}

public sealed class ProviderPriorityRequest
{
    [JsonPropertyName("order")]
    public List<string> Order { get; init; } = [];
}
