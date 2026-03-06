using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Persistence contract for <see cref="MetadataClaim"/> records.
///
/// The <c>metadata_claims</c> table is append-only: rows are NEVER deleted.
/// The full claim history is retained to allow re-scoring when provider weights
/// change (Spec: Phase 4 – Invariants § Claim History).
///
/// Implementations live in <c>MediaEngine.Storage</c>.
/// </summary>
public interface IMetadataClaimRepository
{
    /// <summary>
    /// Inserts a batch of claims into the <c>metadata_claims</c> table.
    /// Each claim is inserted unconditionally; duplicate insertion of the same
    /// logical claim is prevented by the caller, not this method.
    /// </summary>
    /// <param name="claims">The claims to insert. May be empty; no-op if so.</param>
    /// <param name="ct">Cancellation token.</param>
    Task InsertBatchAsync(
        IReadOnlyList<MetadataClaim> claims,
        CancellationToken ct = default);

    /// <summary>
    /// Returns all claims for a given entity, ordered by <see cref="MetadataClaim.ClaimedAt"/>
    /// ascending (oldest first).
    /// </summary>
    /// <param name="entityId">The entity whose claims to retrieve.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<MetadataClaim>> GetByEntityAsync(
        Guid entityId,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes all claims for the given <paramref name="entityId"/>.
    /// Used during orphan cleanup when the asset record is being removed.
    /// </summary>
    Task DeleteByEntityAsync(Guid entityId, CancellationToken ct = default);
}
