namespace MediaEngine.Providers.Contracts;

public enum IdentityPipelineSignalKind
{
    Retail,
    WikidataBridge,
    Hydration,
}

/// <summary>
/// In-process wake-up signal for durable identity pipeline pollers.
/// The database remains the source of truth; this only reduces idle latency.
/// </summary>
public interface IIdentityPipelineSignal
{
    void Signal(IdentityPipelineSignalKind kind);

    Task WaitAsync(
        IdentityPipelineSignalKind kind,
        TimeSpan fallbackDelay,
        CancellationToken ct = default);
}
