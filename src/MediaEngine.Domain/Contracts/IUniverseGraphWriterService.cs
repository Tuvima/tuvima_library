namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Debounced orchestrator for writing <c>universe.xml</c> sidecar files.
///
/// <para>
/// Called after fictional entity enrichment completes. Because a single work
/// hydration may trigger 20+ character enrichments, the write is debounced —
/// a background timer waits a configurable interval (default 5 seconds) after
/// the last enrichment before writing. This prevents thrashing the filesystem
/// with repeated writes during a burst of enrichments.
/// </para>
///
/// <para>
/// Each universe QID gets its own independent debounce timer. Enrichments in
/// one universe do not delay writes for another.
/// </para>
/// </summary>
public interface IUniverseGraphWriterService
{
    /// <summary>
    /// Signals that an entity in the specified universe was enriched.
    /// The service debounces writes — after a configurable quiet period
    /// (default 5 seconds) with no further signals for this universe,
    /// the <c>universe.xml</c> is written.
    /// </summary>
    /// <param name="universeQid">The narrative root QID of the universe.</param>
    /// <param name="ct">Cancellation token.</param>
    Task NotifyEntityEnrichedAsync(string universeQid, CancellationToken ct = default);
}
