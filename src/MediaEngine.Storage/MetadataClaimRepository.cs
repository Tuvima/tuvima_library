using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IMetadataClaimRepository"/>.
/// Uses Dapper for type-safe column-to-property mapping.
///
/// The <c>metadata_claims</c> table is append-only: this repository NEVER
/// issues DELETE or UPDATE statements (except <see cref="DeleteByEntityAsync"/>
/// which is a special-case for entity wipes).  Full claim history is retained
/// to allow re-scoring when provider weights change.
///
/// Spec: Phase 4 – Invariants § Claim History;
///       Phase 9 – External Metadata Adapters § Claim Persistence.
/// </summary>
public sealed class MetadataClaimRepository : IMetadataClaimRepository
{
    private readonly IDatabaseConnection _db;

    public MetadataClaimRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    // -------------------------------------------------------------------------
    // IMetadataClaimRepository
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task InsertBatchAsync(
        IReadOnlyList<MetadataClaim> claims,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (claims.Count == 0)
            return;

        await _db.AcquireWriteLockAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = _db.CreateConnection();
            using var tx   = conn.BeginTransaction();
            try
            {
                const string sql = """
                    INSERT INTO metadata_claims
                        (id, entity_id, provider_id, claim_key, claim_value,
                         confidence, claimed_at, is_user_locked)
                    VALUES
                        (@Id, @EntityId, @ProviderId, @ClaimKey, @ClaimValue,
                         @Confidence, @ClaimedAt, @IsUserLocked);
                    """;

                // Build the batch parameter list — Dapper executes one INSERT per item.
                var rows = claims.Select(c => new
                {
                    Id           = c.Id.ToString(),
                    EntityId     = c.EntityId.ToString(),
                    ProviderId   = c.ProviderId.ToString(),
                    c.ClaimKey,
                    c.ClaimValue,
                    c.Confidence,
                    ClaimedAt    = c.ClaimedAt.ToString("o"),
                    IsUserLocked = c.IsUserLocked ? 1 : 0,
                });

                conn.Execute(sql, rows, transaction: tx);
                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }
        finally
        {
            _db.ReleaseWriteLock();
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<MetadataClaim>> GetByEntityAsync(
        Guid entityId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var results = conn.Query<MetadataClaim>("""
            SELECT id             AS Id,
                   entity_id      AS EntityId,
                   provider_id    AS ProviderId,
                   claim_key      AS ClaimKey,
                   claim_value    AS ClaimValue,
                   confidence     AS Confidence,
                   claimed_at     AS ClaimedAt,
                   is_user_locked AS IsUserLocked
            FROM   metadata_claims
            WHERE  entity_id = @entityId
            ORDER  BY claimed_at ASC;
            """, new { entityId = entityId.ToString() }).AsList();

        return Task.FromResult<IReadOnlyList<MetadataClaim>>(results);
    }

    /// <inheritdoc/>
    public async Task DeleteByEntityAsync(Guid entityId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        await _db.AcquireWriteLockAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = _db.CreateConnection();
            conn.Execute(
                "DELETE FROM metadata_claims WHERE entity_id = @entityId;",
                new { entityId = entityId.ToString() });
        }
        finally
        {
            _db.ReleaseWriteLock();
        }
    }
}
