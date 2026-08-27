using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.System;

public sealed class StartupReadinessResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "not_ready";

    [JsonPropertyName("checked_at")]
    public DateTimeOffset CheckedAt { get; set; }

    [JsonPropertyName("checks")]
    public List<StartupReadinessCheckResponse> Checks { get; set; } = [];
}

public sealed class StartupReadinessCheckResponse
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("data")]
    public Dictionary<string, string> Data { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
