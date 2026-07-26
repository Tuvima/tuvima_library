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

    public Task RecordAsync(
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
            return Task.CompletedTask;

        using var conn = _db.CreateConnection();
        var resolvedBatchId = batchId
            ?? ResolveLatestBatchId(conn, parentEntityId, ct);
        if (!resolvedBatchId.HasValue)
            return Task.CompletedTask;

        conn.Execute("""
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
            });
        return Task.CompletedTask;
    }

    private static Guid? ResolveLatestBatchId(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        Guid? parentEntityId,
        CancellationToken ct)
    {
        if (!parentEntityId.HasValue)
            return null;

        ct.ThrowIfCancellationRequested();
        return conn.ExecuteScalar<Guid?>("""
            SELECT ingestion_run_id
            FROM identity_jobs
            WHERE entity_id = @entityId
              AND ingestion_run_id IS NOT NULL
            ORDER BY updated_at DESC, created_at DESC
            LIMIT 1;
            """, new { entityId = parentEntityId.Value });
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
