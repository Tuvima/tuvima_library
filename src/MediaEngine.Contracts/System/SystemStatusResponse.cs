using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.System;

public sealed class SystemStatusResponse
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("language")]
    public string Language { get; init; } = "en";
}
