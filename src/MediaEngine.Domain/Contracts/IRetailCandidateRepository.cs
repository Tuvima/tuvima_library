using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Persistence contract for the <c>retail_match_candidates</c> table.
///
/// Stores every candidate returned by retail providers during Stage 1,
/// with full score breakdowns. The Action Center uses these records to
/// show the user all options and why each scored the way it did.
/// </summary>
public interface IRetailCandidateRepository
{
    /// <summary>Bulk-inserts all candidates from a Stage 1 run.</summary>
    Task InsertBatchAsync(IReadOnlyList<RetailMatchCandidate> candidates, CancellationToken ct = default);

    /// <summary>Returns all candidates for a given job, ordered by score descending.</summary>
    Task<IReadOnlyList<RetailMatchCandidate>> GetByJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>Returns the accepted candidate for a job (outcome = "AutoAccepted" or "Ambiguous").</summary>
    Task<RetailMatchCandidate?> GetSelectedAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>Returns a single candidate by its primary key.</summary>
    Task<RetailMatchCandidate?> GetByIdAsync(Guid candidateId, CancellationToken ct = default);
}
