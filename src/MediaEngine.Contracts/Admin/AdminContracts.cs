using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.Admin;

public sealed class ApiKeyDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; init; } = "Administrator";

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class CreateApiKeyRequest
{
    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    [JsonPropertyName("role")]
    public string? Role { get; init; }
}

public sealed class CreateApiKeyResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; init; } = "Administrator";

    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class RevokeAllKeysResponse
{
    [JsonPropertyName("revoked_count")]
    public int RevokedCount { get; init; }
}

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
