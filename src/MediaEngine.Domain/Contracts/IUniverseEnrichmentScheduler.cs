using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Queues inline Stage 3 universe enrichment work and exposes a maintenance trigger.
/// </summary>
public interface IUniverseEnrichmentScheduler
{
    /// <summary>
    /// Queues a file for coalesced inline Stage 3 processing after quick hydration completes.
    /// </summary>
    ValueTask QueueInlineAsync(UniverseEnrichmentRequest request, CancellationToken ct = default);

    /// <summary>
    /// Signals the maintenance sweep to run immediately.
    /// </summary>
    void TriggerManualSweep();
}
