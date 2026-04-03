using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Persistence contract for the <c>wikidata_bridge_candidates</c> table.
///
/// Stores every Wikidata entity evaluated during Stage 2 bridge resolution,
/// with match method and score breakdown. The review drawer uses these records
/// for user disambiguation when multiple QIDs are viable.
/// </summary>
public interface IWikidataCandidateRepository
{
    /// <summary>Bulk-inserts all candidates from a Stage 2 run.</summary>
    Task InsertBatchAsync(IReadOnlyList<WikidataBridgeCandidate> candidates, CancellationToken ct = default);

    /// <summary>Returns all candidates for a given job, ordered by score descending.</summary>
    Task<IReadOnlyList<WikidataBridgeCandidate>> GetByJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>Returns the accepted candidate for a job (outcome = "AutoAccepted").</summary>
    Task<WikidataBridgeCandidate?> GetSelectedAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>Returns a single candidate by its primary key.</summary>
    Task<WikidataBridgeCandidate?> GetByIdAsync(Guid candidateId, CancellationToken ct = default);
}
