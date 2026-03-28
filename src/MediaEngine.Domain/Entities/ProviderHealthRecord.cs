namespace MediaEngine.Domain.Entities;

using MediaEngine.Domain.Enums;

/// <summary>
/// Persistent health state for a single metadata provider.
/// One row per registered provider.
/// </summary>
public sealed class ProviderHealthRecord
{
    /// <summary>Provider identifier (matches config name, e.g. "metron", "tmdb").</summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>Current health status.</summary>
    public ProviderHealthStatus Status { get; set; } = ProviderHealthStatus.Healthy;

    /// <summary>Number of consecutive failed requests.</summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary>When the last health check (active or passive) occurred.</summary>
    public DateTimeOffset? LastCheckAt { get; set; }

    /// <summary>When the last successful request completed.</summary>
    public DateTimeOffset? LastSuccessAt { get; set; }

    /// <summary>When the last failure occurred.</summary>
    public DateTimeOffset? LastFailureAt { get; set; }

    /// <summary>Human-readable reason for the last failure.</summary>
    public string? LastFailureReason { get; set; }

    /// <summary>When the next active health probe should run.</summary>
    public DateTimeOffset? NextCheckAt { get; set; }

    /// <summary>When the provider first entered the Down state (null when healthy).</summary>
    public DateTimeOffset? DownSince { get; set; }
}
