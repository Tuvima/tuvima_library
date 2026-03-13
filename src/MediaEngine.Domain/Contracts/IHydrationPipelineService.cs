using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Contract for the three-stage hydration pipeline.
///
/// Replaces the flat "first provider wins" approach with a sequential pipeline:
/// <list type="number">
///   <item><b>Stage 1 — Authority Match:</b> Wikidata QID resolution via bridge IDs
///     (ISBN, ASIN, TMDB ID) or title search. SPARQL deep hydration fetches
///     core properties. Hub intelligence + person enrichment run here.</item>
///   <item><b>Stage 2 — Context Match:</b> Wikipedia article summary via QID
///     sitelink lookup. Deposits description claim.</item>
///   <item><b>Stage 3 — Retail Match:</b> Retail providers run in waterfall order
///     from config/slots.json. Uses bridge IDs from Stage 1 for precise
///     cover art and rating lookups.</item>
/// </list>
///
/// Two entry points:
/// <list type="bullet">
///   <item><see cref="EnqueueAsync"/> — non-blocking, queues to an internal channel
///     for background processing (used by the ingestion pipeline).</item>
///   <item><see cref="RunSynchronousAsync"/> — blocking, bypasses the queue for
///     immediate results (used by user-triggered hydration and review resolution).</item>
/// </list>
///
/// Implementations live in <c>MediaEngine.Providers</c>.
/// </summary>
public interface IHydrationPipelineService
{
    /// <summary>
    /// Enqueues a harvest request for asynchronous three-stage processing.
    /// Returns immediately — the caller does not wait for the pipeline to complete.
    ///
    /// The underlying channel is bounded (capacity 500, DropOldest policy) to
    /// prevent memory growth during heavy ingestion bursts.
    /// </summary>
    /// <param name="request">The harvest request to enqueue.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask EnqueueAsync(HarvestRequest request, CancellationToken ct = default);

    /// <summary>
    /// Runs the full three-stage hydration pipeline synchronously and returns
    /// the result. Bypasses the internal channel queue.
    ///
    /// Used for user-triggered hydration (Dashboard "Hydrate" button) and
    /// review resolution (user selects a QID candidate).
    ///
    /// When <see cref="HarvestRequest.PreResolvedQid"/> is set, Stage 2 skips
    /// QID resolution and goes straight to SPARQL deep hydration.
    /// </summary>
    /// <param name="request">The harvest request to process.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A result summarising what each stage accomplished.</returns>
    Task<HydrationResult> RunSynchronousAsync(
        HarvestRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// The approximate number of harvest requests currently waiting in the
    /// background queue. Useful for monitoring and diagnostics only.
    /// </summary>
    int PendingCount { get; }
}
