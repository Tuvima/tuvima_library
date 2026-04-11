namespace MediaEngine.Providers.Workers;

/// <summary>
/// Strongly typed metrics for a single Stage 2 poll cycle.
/// Passed through the worker flow as a mutable context; emitted as
/// a structured log summary at the end of <c>PollAsync</c>.
/// </summary>
public sealed class Stage2PollMetrics
{
    // Job counts
    public int LeasedJobs { get; set; }
    public Dictionary<string, int> JobsByMediaType { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int UniqueGroupingKeys { get; set; }

    // Resolution phase
    public int ResolveBatchCalls { get; set; }
    public long ResolveDurationMs { get; set; }
    public int ResolvedJobs { get; set; }
    public int UniqueResolvedQids { get; set; }

    // Phase 6 — per-job fetch
    public int Phase6FetchCalls { get; set; }
    public int Phase6DuplicateSavings { get; set; }

    // Child discovery
    public int ChildDiscoveryCalls { get; set; }

    // Outcomes
    public int Failures { get; set; }
    public int Retries { get; set; }
    public long TotalPollDurationMs { get; set; }

    // Adapter-level counters
    public int ExtendCalls { get; set; }
    public int ExtendCacheHits { get; set; }
    public int LabelFetchCalls { get; set; }

    /// <summary>
    /// Returns a formatted summary string for structured logging.
    /// </summary>
    public override string ToString() =>
        $"Stage2 poll: {LeasedJobs} jobs, {UniqueResolvedQids} unique QIDs, " +
        $"resolve: {ResolveDurationMs}ms ({ResolveBatchCalls} calls), " +
        $"fetch: {Phase6FetchCalls} calls ({Phase6DuplicateSavings} dedup savings, " +
        $"{ExtendCacheHits} cache hits), " +
        $"child: {ChildDiscoveryCalls} calls, " +
        $"failures: {Failures}, retries: {Retries}, " +
        $"total: {TotalPollDurationMs}ms";
}
