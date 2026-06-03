using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite ledger for artifacts created during an ingestion batch.
/// </summary>
public sealed class IngestionBatchArtifactRepository : IIngestionBatchArtifactRepository
{
    private readonly IDatabaseConnection _db;

    public IngestionBatchArtifactRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    public async Task RecordAsync(
        Guid? batchId,
        string artifactType,
        Guid? artifactId,
        Guid? parentEntityId,
        string? parentEntityType,
        string action,
        string? displayName,
        string? providerId,
        string? source,
        string? detailJson,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(artifactType) || string.IsNullOrWhiteSpace(action))
            return;

        using var conn = _db.CreateConnection();
        var resolvedBatchId = batchId
            ?? await ResolveLatestBatchIdAsync(conn, parentEntityId, ct).ConfigureAwait(false);
        if (!resolvedBatchId.HasValue)
            return;

        await conn.ExecuteAsync("""
            INSERT INTO ingestion_batch_artifacts
                (id, batch_id, artifact_type, artifact_id, parent_entity_id, parent_entity_type,
                 action, display_name, provider_id, source, detail_json, occurred_at)
            VALUES
                (@id, @batchId, @artifactType, @artifactId, @parentEntityId, @parentEntityType,
                 @action, @displayName, @providerId, @source, @detailJson, @occurredAt);
            """, new
            {
                id = Guid.NewGuid(),
                batchId = resolvedBatchId.Value,
                artifactType = artifactType.Trim(),
                artifactId,
                parentEntityId,
                parentEntityType = NullIfBlank(parentEntityType),
                action = action.Trim(),
                displayName = NullIfBlank(displayName),
                providerId = NullIfBlank(providerId),
                source = NullIfBlank(source),
                detailJson = NullIfBlank(detailJson),
                occurredAt = DateTimeOffset.UtcNow.ToString("O"),
            }).ConfigureAwait(false);
    }

    private static async Task<Guid?> ResolveLatestBatchIdAsync(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        Guid? parentEntityId,
        CancellationToken ct)
    {
        if (!parentEntityId.HasValue)
            return null;

        ct.ThrowIfCancellationRequested();
        return await conn.ExecuteScalarAsync<Guid?>("""
            SELECT ingestion_run_id
            FROM identity_jobs
            WHERE entity_id = @entityId
              AND ingestion_run_id IS NOT NULL
            ORDER BY updated_at DESC, created_at DESC
            LIMIT 1;
            """, new { entityId = parentEntityId.Value }).ConfigureAwait(false);
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
