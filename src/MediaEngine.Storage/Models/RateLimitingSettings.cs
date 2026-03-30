using System.Text.Json.Serialization;

namespace MediaEngine.Storage.Models;

/// <summary>
/// Rate limiting policy parameters for the Engine API.
/// Loaded from <c>config/core.json</c> (<c>rate_limiting</c> section).
/// </summary>
public sealed class RateLimitingSettings
{
    /// <summary>API key generation: strict limit to prevent brute-force.</summary>
    [JsonPropertyName("key_generation")]
    public RateLimitPolicy KeyGeneration { get; set; } = new() { PermitLimit = 5, WindowMinutes = 1 };

    /// <summary>File streaming: higher limit for media playback.</summary>
    [JsonPropertyName("streaming")]
    public RateLimitPolicy Streaming { get; set; } = new() { PermitLimit = 100, WindowMinutes = 1 };

    /// <summary>General API: default limit for all other endpoints.</summary>
    [JsonPropertyName("general")]
    public RateLimitPolicy General { get; set; } = new() { PermitLimit = 60, WindowMinutes = 1 };
}

/// <summary>A single rate limit policy with a permit count and time window.</summary>
public sealed class RateLimitPolicy
{
    [JsonPropertyName("permit_limit")]
    public int PermitLimit { get; set; }

    [JsonPropertyName("window_minutes")]
    public int WindowMinutes { get; set; } = 1;
}
