using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Dispatches enrichment work to specialized workers based on pass type.
///
/// This is a thin orchestrator — no enrichment logic lives here. Each worker
/// is its own class with its own dependencies, injected via DI and independently
/// testable. Adding a new enrichment type means adding one worker and registering
/// it here — no pipeline worker changes required.
/// </summary>
public interface IEnrichmentService
{
    /// <summary>
    /// Quick pass: core identity visible on the Dashboard fast.
    /// Runs cover art download, person enrichment, and write-back.
    /// </summary>
    Task RunQuickPassAsync(Guid entityId, string qid, CancellationToken ct = default);

    /// <summary>
    /// Universe pass: deep enrichment in background.
    /// Runs child entity discovery, fictional entity enrichment, person actor-character
    /// mapping, image enrichment, description enrichment, and write-back.
    /// </summary>
    Task RunUniversePassAsync(Guid entityId, string qid, CancellationToken ct = default);

    /// <summary>
    /// Core Stage 3 universe pass that gates file-level completion.
    /// Runs child discovery, fictional entity enrichment, and actor-character mapping.
    /// </summary>
    Task RunUniverseCorePassAsync(Guid entityId, string qid, CancellationToken ct = default);

    /// <summary>
    /// Non-blocking Stage 3 enhancers that improve a file after core universe work completes.
    /// Runs image enrichment, description enrichment, and write-back.
    /// </summary>
    Task RunUniverseEnhancerPassAsync(Guid entityId, string qid, CancellationToken ct = default);

    /// <summary>
    /// Targeted re-run: re-run one enrichment type for one entity.
    /// Useful for "re-download cover art" or "re-fetch persons" from the Dashboard.
    /// </summary>
    Task RunSingleEnrichmentAsync(Guid entityId, string qid, EnrichmentType type, CancellationToken ct = default);
}
