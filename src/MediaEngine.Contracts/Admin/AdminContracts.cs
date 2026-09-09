using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.Admin;

public sealed class ProviderConfigDto
{
    [JsonPropertyName("provider_id")]
    public string ProviderId { get; init; } = string.Empty;

    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; init; } = string.Empty;

    [JsonPropertyName("is_secret")]
    public bool IsSecret { get; init; }
}

public sealed class UpsertProviderConfigRequest
{
    [JsonPropertyName("value")]
    public string Value { get; init; } = string.Empty;

    [JsonPropertyName("is_secret")]
    public bool IsSecret { get; init; }
}
