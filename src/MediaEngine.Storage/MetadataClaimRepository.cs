using Microsoft.Data.Sqlite;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// ORM-less SQLite implementation of <see cref="IMetadataClaimRepository"/>.
///
/// The <c>metadata_claims</c> table is append-only: this repository NEVER
/// issues DELETE or UPDATE statements.  Full claim history is retained to
/// allow re-scoring when provider weights change.
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
            var conn = _db.Open();

            // Wrap the batch in a single transaction for atomicity + performance.
            using var tx = conn.BeginTransaction();
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO metadata_claims
                        (id, entity_id, provider_id, claim_key, claim_value,
                         confidence, claimed_at, is_user_locked)
                    VALUES
                        (@id, @entity_id, @provider_id, @claim_key, @claim_value,
                         @confidence, @claimed_at, @is_user_locked);
                    """;

                // Create parameters once and rebind values per row.
                var pId            = cmd.Parameters.Add("@id",             SqliteType.Text);
                var pEntityId      = cmd.Parameters.Add("@entity_id",      SqliteType.Text);
                var pProviderId    = cmd.Parameters.Add("@provider_id",    SqliteType.Text);
                var pClaimKey      = cmd.Parameters.Add("@claim_key",      SqliteType.Text);
                var pClaimValue    = cmd.Parameters.Add("@claim_value",    SqliteType.Text);
                var pConfidence    = cmd.Parameters.Add("@confidence",     SqliteType.Real);
                var pClaimedAt     = cmd.Parameters.Add("@claimed_at",     SqliteType.Text);
                var pIsUserLocked  = cmd.Parameters.Add("@is_user_locked", SqliteType.Integer);

                foreach (var claim in claims)
                {
                    ct.ThrowIfCancellationRequested();

                    pId.Value           = claim.Id.ToString();
                    pEntityId.Value     = claim.EntityId.ToString();
                    pProviderId.Value   = claim.ProviderId.ToString();
                    pClaimKey.Value     = claim.ClaimKey;
                    pClaimValue.Value   = claim.ClaimValue;
                    pConfidence.Value   = claim.Confidence;
                    pClaimedAt.Value    = claim.ClaimedAt.ToString("o"); // ISO-8601 round-trip
                    pIsUserLocked.Value = claim.IsUserLocked ? 1 : 0;

                    cmd.ExecuteNonQuery();
                }

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

        var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, entity_id, provider_id, claim_key, claim_value,
                   confidence, claimed_at, is_user_locked
            FROM   metadata_claims
            WHERE  entity_id = @entity_id
            ORDER  BY claimed_at ASC;
            """;
        cmd.Parameters.AddWithValue("@entity_id", entityId.ToString());

        var results = new List<MetadataClaim>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            results.Add(MapRow(reader));
        }

        return Task.FromResult<IReadOnlyList<MetadataClaim>>(results);
    }

    // -------------------------------------------------------------------------
    // Private row mapper
    // -------------------------------------------------------------------------

    /// <summary>
    /// <inheritdoc/>
    public async Task DeleteByEntityAsync(Guid entityId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        await _db.AcquireWriteLockAsync(ct).ConfigureAwait(false);
        try
        {
            var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM metadata_claims WHERE entity_id = @entity_id;";
            cmd.Parameters.AddWithValue("@entity_id", entityId.ToString());
            cmd.ExecuteNonQuery();
        }
        finally
        {
            _db.ReleaseWriteLock();
        }
    }

    /// Maps the current reader row to a <see cref="MetadataClaim"/>.
    /// Column ordinals match the SELECT list used in every query in this class:
    ///   0=id, 1=entity_id, 2=provider_id, 3=claim_key, 4=claim_value,
    ///   5=confidence, 6=claimed_at, 7=is_user_locked
    /// </summary>
    private static MetadataClaim MapRow(SqliteDataReader r) => new()
    {
        Id            = Guid.Parse(r.GetString(0)),
        EntityId      = Guid.Parse(r.GetString(1)),
        ProviderId    = Guid.Parse(r.GetString(2)),
        ClaimKey      = r.GetString(3),
        ClaimValue    = r.GetString(4),
        Confidence    = r.GetDouble(5),
        ClaimedAt     = DateTimeOffset.Parse(r.GetString(6)),
        IsUserLocked  = r.GetInt64(7) != 0,
    };
}
