namespace MediaEngine.Domain.Enums;

/// <summary>
/// Tracks whether a metadata provider is reachable.
/// </summary>
public enum ProviderHealthStatus
{
    /// <summary>Provider is responding normally.</summary>
    Healthy = 0,

    /// <summary>Intermittent failures detected (1–2 consecutive).</summary>
    Degraded = 1,

    /// <summary>Provider unreachable (3+ consecutive failures).</summary>
    Down = 2,
}
