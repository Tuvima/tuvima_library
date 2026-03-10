namespace MediaEngine.Domain.Models;

/// <summary>
/// Hardware video encoding/decoding capabilities detected from the local FFmpeg installation.
/// </summary>
public sealed record HardwareCapabilities
{
    /// <summary>NVIDIA NVENC GPU encoder available.</summary>
    public bool HasNvenc { get; init; }

    /// <summary>Intel Quick Sync Video encoder available.</summary>
    public bool HasQuickSync { get; init; }

    /// <summary>AMD/Intel VAAPI encoder available (Linux).</summary>
    public bool HasVaapi { get; init; }

    /// <summary>
    /// The preferred encoder name for FFmpeg based on detected hardware.
    /// Defaults to <c>libx264</c> (software) when no hardware encoder is found.
    /// </summary>
    public string PreferredEncoder { get; init; } = "libx264";

    /// <summary>Human-readable description of the detected accelerator, or null.</summary>
    public string? DetectedAccelerator { get; init; }
}
