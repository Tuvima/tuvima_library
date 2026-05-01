using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Coordinates app-wide concurrency for external enrichment work.
/// </summary>
public interface IEnrichmentConcurrencyLimiter
{
    Task RunAsync(EnrichmentWorkKind kind, Func<CancellationToken, Task> operation, CancellationToken ct = default);

    Task<T> RunAsync<T>(EnrichmentWorkKind kind, Func<CancellationToken, Task<T>> operation, CancellationToken ct = default);

    int GetAvailableSlots(EnrichmentWorkKind kind);
}

/// <summary>
/// Default pass-through limiter used by tests and hosts that do not register throttling.
/// </summary>
public sealed class NoopEnrichmentConcurrencyLimiter : IEnrichmentConcurrencyLimiter
{
    public static NoopEnrichmentConcurrencyLimiter Instance { get; } = new();

    private NoopEnrichmentConcurrencyLimiter() { }

    public Task RunAsync(EnrichmentWorkKind kind, Func<CancellationToken, Task> operation, CancellationToken ct = default)
        => operation(ct);

    public Task<T> RunAsync<T>(EnrichmentWorkKind kind, Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
        => operation(ct);

    public int GetAvailableSlots(EnrichmentWorkKind kind) => int.MaxValue;
}
