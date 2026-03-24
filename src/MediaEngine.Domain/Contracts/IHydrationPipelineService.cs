using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Contract for the two-stage hydration pipeline.
///
/// <list type="number">
///   <item><b>Stage 1 — Retail Identification:</b> Retail providers (Apple Books,
///     TMDB, MusicBrainz) search using file metadata (ISBN, ASIN, title+author).
///     Results are scored against file metadata for auto-accept or review queue
///     routing. Deposits cover art, description, and bridge IDs.</item>
///   <item><b>Stage 2 — Wikidata Bridge Resolution:</b> Uses bridge IDs from
///     Stage 1 to resolve Wikidata edition and work QIDs via the Reconciliation
///     API. Runs as a deduplicated batch after all files in the ingestion batch
///     complete Stage 1. Links editions to works, works to universes.</item>
/// </list>
///
/// Three entry points:
/// <list type="bullet">
///   <item><see cref="EnqueueAsync"/> — non-blocking, queues to an internal channel
///     for background processing (used by the ingestion pipeline).</item>
///   <item><see cref="RunSynchronousAsync"/> — blocking, bypasses the queue for
///     immediate results (used by user-triggered hydration and review resolution).</item>
///   <item><see cref="RunBatchBridgeResolutionAsync"/> — runs Stage 2 for all files
///     in an ingestion batch after Stage 1 completes.</item>
/// </list>
///
/// Implementations live in <c>MediaEngine.Providers</c>.
/// </summary>
public interface IHydrationPipelineService
{
    /// <summary>
    /// Enqueues a harvest request for asynchronous two-stage processing.
    /// Returns immediately — the caller does not wait for the pipeline to complete.
    ///
    /// The underlying channel is bounded (capacity 500, DropOldest policy) to
    /// prevent memory growth during heavy ingestion bursts.
    /// </summary>
    /// <param name="request">The harvest request to enqueue.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask EnqueueAsync(HarvestRequest request, CancellationToken ct = default);

    /// <summary>
    /// Runs the full two-stage hydration pipeline synchronously and returns
    /// the result. Bypasses the internal channel queue.
    ///
    /// Used for user-triggered hydration (Dashboard "Hydrate" button) and
    /// review resolution (user selects a QID candidate).
    ///
    /// When <see cref="HarvestRequest.PreResolvedQid"/> is set, Stage 1 skips
    /// retail search and uses the pre-resolved QID directly.
    /// </summary>
    /// <param name="request">The harvest request to process.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A result summarising what each stage accomplished.</returns>
    Task<HydrationResult> RunSynchronousAsync(
        HarvestRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Runs Stage 2 (Wikidata Bridge Resolution) for all files in an ingestion
    /// batch. Called after all files complete Stage 1. Deduplicates shared
    /// entities so each Wikidata QID is resolved only once per batch.
    /// </summary>
    /// <param name="batchId">The ingestion batch identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RunBatchBridgeResolutionAsync(Guid batchId, CancellationToken ct = default);

    /// <summary>
    /// The approximate number of harvest requests currently waiting in the
    /// background queue. Useful for monitoring and diagnostics only.
    /// </summary>
    int PendingCount { get; }
}
