using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Persistence contract for the <c>identity_jobs</c> table.
///
/// Identity jobs track each media asset through the retail-first pipeline:
/// Stage 1 (retail match), Stage 2 (Wikidata bridge), and Quick hydration.
/// Jobs are durable — they survive engine restarts and replace the former
/// in-memory <c>BoundedChannel</c> queue.
///
/// Workers lease jobs atomically via <see cref="LeaseNextAsync"/> to prevent
/// double-processing. Leases expire after a configurable timeout so stuck
/// jobs are automatically retried.
/// </summary>
public interface IIdentityJobRepository
{
    /// <summary>Creates a new identity job.</summary>
    Task CreateAsync(IdentityJob job, CancellationToken ct = default);

    /// <summary>Returns the identity job for a given entity, if one exists.</summary>
    Task<IdentityJob?> GetByEntityAsync(Guid entityId, CancellationToken ct = default);

    /// <summary>Returns a job by its primary key.</summary>
    Task<IdentityJob?> GetByIdAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Atomically leases up to <paramref name="batchSize"/> jobs in the specified
    /// <paramref name="states"/> for the named <paramref name="workerName"/>.
    /// Only jobs with no active lease (or an expired lease) are eligible.
    /// When <paramref name="excludeRunIds"/> is non-empty, jobs belonging to
    /// those ingestion runs are skipped (batch gate). Jobs with a NULL
    /// <c>ingestion_run_id</c> (ad-hoc / manual) are never excluded.
    /// </summary>
    Task<IReadOnlyList<IdentityJob>> LeaseNextAsync(
        string workerName,
        IReadOnlyList<IdentityJobState> states,
        int batchSize,
        TimeSpan leaseDuration,
        IReadOnlyList<string>? excludeRunIds = null,
        CancellationToken ct = default);

    /// <summary>Transitions the job to a new state and clears the lease.</summary>
    Task UpdateStateAsync(Guid jobId, IdentityJobState newState, string? error = null, CancellationToken ct = default);

    /// <summary>Sets the accepted retail candidate on the job.</summary>
    Task SetSelectedCandidateAsync(Guid jobId, Guid candidateId, CancellationToken ct = default);

    /// <summary>Sets the confirmed Wikidata QID on the job.</summary>
    Task SetResolvedQidAsync(Guid jobId, string qid, CancellationToken ct = default);

    /// <summary>Returns jobs that have been stuck (lease expired, state not terminal) for retry.</summary>
    Task<IReadOnlyList<IdentityJob>> GetStaleAsync(TimeSpan age, int limit, CancellationToken ct = default);

    /// <summary>
    /// Finds jobs stuck in intermediate processing states (RetailSearching,
    /// BridgeSearching, Hydrating) that have no active lease and have been
    /// in that state for longer than <paramref name="stuckThreshold"/>.
    /// Resets them to the appropriate "ready" state so the next poll picks
    /// them up. Returns the number of jobs reclaimed.
    /// </summary>
    Task<int> ReclaimStuckJobsAsync(TimeSpan stuckThreshold, CancellationToken ct = default);

    /// <summary>Returns jobs in a specific state, ordered by creation time.</summary>
    Task<IReadOnlyList<IdentityJob>> GetByStateAsync(IdentityJobState state, int limit, CancellationToken ct = default);

    /// <summary>Returns job state counts grouped by ingestion run, for batch progress reporting.</summary>
    Task<IReadOnlyDictionary<string, int>> GetStateCountsByRunAsync(Guid ingestionRunId, CancellationToken ct = default);

    /// <summary>
    /// Returns the count of Stage 1 jobs still pending (Queued or RetailSearching)
    /// for each of the supplied ingestion run IDs.
    /// Runs with zero pending jobs are omitted from the result.
    /// Used by the batch gate to determine which runs are still draining Stage 1.
    /// </summary>
    Task<IReadOnlyDictionary<string, int>> GetPendingStage1CountsByRunAsync(
        IReadOnlyList<string> ingestionRunIds, CancellationToken ct = default);

    /// <summary>Releases the lease on a job without changing state (e.g. on graceful shutdown).</summary>
    Task ReleasLeaseAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Returns the count of identity jobs in non-terminal states (active pipeline work).
    /// Used by DeferredEnrichmentService to yield to Pass 1 ingestion.
    /// </summary>
    Task<int> CountActiveAsync(CancellationToken ct = default);
}
