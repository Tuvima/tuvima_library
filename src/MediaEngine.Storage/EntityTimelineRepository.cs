using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;
using Microsoft.Data.Sqlite;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IEntityTimelineRepository"/>.
/// Manages the <c>entity_events</c> and <c>entity_field_changes</c> tables.
/// </summary>
public sealed class EntityTimelineRepository : IEntityTimelineRepository
{
    private readonly IDatabaseConnection _db;

    public EntityTimelineRepository(IDatabaseConnection db) => _db = db;

    // ── Events ─────────────────────────────────────────────────────────

    public async Task InsertEventAsync(EntityEvent evt, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        await InsertEventCoreAsync(conn, evt, ct).ConfigureAwait(false);
    }

    public async Task InsertEventsAsync(IReadOnlyList<EntityEvent> events, CancellationToken ct = default)
    {
        if (events.Count == 0) return;
        using var conn = _db.CreateConnection();
        await using var tx = conn.BeginTransaction();
        foreach (var evt in events)
            await InsertEventCoreAsync(conn, evt, ct).ConfigureAwait(false);
        tx.Commit();
    }

    private static async Task InsertEventCoreAsync(SqliteConnection conn, EntityEvent evt, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO entity_events (
                id, entity_id, entity_type, event_type, stage, trigger,
                provider_id, provider_name, bridge_id_type, bridge_id_value,
                resolved_qid, confidence,
                score_title, score_author, score_year, score_format,
                score_cross_field, score_cover_art, score_composite,
                occurred_at, ingestion_run_id, detail
            ) VALUES (
                @id, @entity_id, @entity_type, @event_type, @stage, @trigger,
                @provider_id, @provider_name, @bridge_id_type, @bridge_id_value,
                @resolved_qid, @confidence,
                @score_title, @score_author, @score_year, @score_format,
                @score_cross_field, @score_cover_art, @score_composite,
                @occurred_at, @ingestion_run_id, @detail
            )
            """;

        await using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", evt.Id.ToString());
        cmd.Parameters.AddWithValue("@entity_id", evt.EntityId.ToString());
        cmd.Parameters.AddWithValue("@entity_type", evt.EntityType);
        cmd.Parameters.AddWithValue("@event_type", evt.EventType);
        cmd.Parameters.AddWithValue("@stage", evt.Stage.HasValue ? (object)evt.Stage.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@trigger", evt.Trigger);
        cmd.Parameters.AddWithValue("@provider_id", (object?)evt.ProviderId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@provider_name", (object?)evt.ProviderName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@bridge_id_type", (object?)evt.BridgeIdType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@bridge_id_value", (object?)evt.BridgeIdValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@resolved_qid", (object?)evt.ResolvedQid ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@confidence", evt.Confidence.HasValue ? (object)evt.Confidence.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@score_title", evt.ScoreTitle.HasValue ? (object)evt.ScoreTitle.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@score_author", evt.ScoreAuthor.HasValue ? (object)evt.ScoreAuthor.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@score_year", evt.ScoreYear.HasValue ? (object)evt.ScoreYear.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@score_format", evt.ScoreFormat.HasValue ? (object)evt.ScoreFormat.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@score_cross_field", evt.ScoreCrossField.HasValue ? (object)evt.ScoreCrossField.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@score_cover_art", evt.ScoreCoverArt.HasValue ? (object)evt.ScoreCoverArt.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@score_composite", evt.ScoreComposite.HasValue ? (object)evt.ScoreComposite.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@occurred_at", evt.OccurredAt.ToString("o"));
        cmd.Parameters.AddWithValue("@ingestion_run_id", evt.IngestionRunId.HasValue ? (object)evt.IngestionRunId.Value.ToString() : DBNull.Value);
        cmd.Parameters.AddWithValue("@detail", (object?)evt.Detail ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<EntityEvent>> GetEventsByEntityAsync(Guid entityId, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        const string sql = """
            SELECT id, entity_id, entity_type, event_type, stage, trigger,
                   provider_id, provider_name, bridge_id_type, bridge_id_value,
                   resolved_qid, confidence,
                   score_title, score_author, score_year, score_format,
                   score_cross_field, score_cover_art, score_composite,
                   occurred_at, ingestion_run_id, detail
            FROM entity_events
            WHERE entity_id = @entity_id
            ORDER BY occurred_at DESC
            """;

        await using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@entity_id", entityId.ToString());
        return await ReadEventsAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<EntityEvent?> GetLatestEventAsync(Guid entityId, int stage, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        const string sql = """
            SELECT id, entity_id, entity_type, event_type, stage, trigger,
                   provider_id, provider_name, bridge_id_type, bridge_id_value,
                   resolved_qid, confidence,
                   score_title, score_author, score_year, score_format,
                   score_cross_field, score_cover_art, score_composite,
                   occurred_at, ingestion_run_id, detail
            FROM entity_events
            WHERE entity_id = @entity_id AND stage = @stage
            ORDER BY occurred_at DESC
            LIMIT 1
            """;

        await using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@entity_id", entityId.ToString());
        cmd.Parameters.AddWithValue("@stage", stage);
        var results = await ReadEventsAsync(cmd, ct).ConfigureAwait(false);
        return results.Count > 0 ? results[0] : null;
    }

    public async Task<IReadOnlyList<EntityEvent>> GetCurrentPipelineStateAsync(Guid entityId, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        // Most recent event per stage for this entity (stages 0-3).
        const string sql = """
            SELECT id, entity_id, entity_type, event_type, stage, trigger,
                   provider_id, provider_name, bridge_id_type, bridge_id_value,
                   resolved_qid, confidence,
                   score_title, score_author, score_year, score_format,
                   score_cross_field, score_cover_art, score_composite,
                   occurred_at, ingestion_run_id, detail
            FROM entity_events e1
            WHERE entity_id = @entity_id
              AND stage IS NOT NULL
              AND occurred_at = (
                  SELECT MAX(e2.occurred_at) FROM entity_events e2
                  WHERE e2.entity_id = e1.entity_id AND e2.stage = e1.stage
              )
            ORDER BY stage ASC
            """;

        await using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@entity_id", entityId.ToString());
        return await ReadEventsAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<EntityEvent?> GetEventByIdAsync(Guid eventId, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        const string sql = """
            SELECT id, entity_id, entity_type, event_type, stage, trigger,
                   provider_id, provider_name, bridge_id_type, bridge_id_value,
                   resolved_qid, confidence,
                   score_title, score_author, score_year, score_format,
                   score_cross_field, score_cover_art, score_composite,
                   occurred_at, ingestion_run_id, detail
            FROM entity_events
            WHERE id = @id
            """;

        await using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", eventId.ToString());
        var results = await ReadEventsAsync(cmd, ct).ConfigureAwait(false);
        return results.Count > 0 ? results[0] : null;
    }

    // ── Field Changes ──────────────────────────────────────────────────

    public async Task InsertFieldChangesAsync(IReadOnlyList<EntityFieldChange> changes, CancellationToken ct = default)
    {
        if (changes.Count == 0) return;
        using var conn = _db.CreateConnection();
        await using var tx = conn.BeginTransaction();

        const string sql = """
            INSERT INTO entity_field_changes (
                id, event_id, entity_id, field,
                old_value, new_value, old_provider_id, new_provider_id,
                confidence, is_file_original
            ) VALUES (
                @id, @event_id, @entity_id, @field,
                @old_value, @new_value, @old_provider_id, @new_provider_id,
                @confidence, @is_file_original
            )
            """;

        foreach (var change in changes)
        {
            await using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", change.Id.ToString());
            cmd.Parameters.AddWithValue("@event_id", change.EventId.ToString());
            cmd.Parameters.AddWithValue("@entity_id", change.EntityId.ToString());
            cmd.Parameters.AddWithValue("@field", change.Field);
            cmd.Parameters.AddWithValue("@old_value", (object?)change.OldValue ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@new_value", (object?)change.NewValue ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@old_provider_id", (object?)change.OldProviderId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@new_provider_id", (object?)change.NewProviderId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@confidence", change.Confidence.HasValue ? (object)change.Confidence.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@is_file_original", change.IsFileOriginal ? 1 : 0);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        tx.Commit();
    }

    public async Task<IReadOnlyList<EntityFieldChange>> GetFieldChangesByEventAsync(Guid eventId, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        const string sql = """
            SELECT id, event_id, entity_id, field,
                   old_value, new_value, old_provider_id, new_provider_id,
                   confidence, is_file_original
            FROM entity_field_changes
            WHERE event_id = @event_id
            ORDER BY field ASC
            """;

        await using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@event_id", eventId.ToString());
        return await ReadFieldChangesAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<EntityFieldChange>> GetFieldHistoryAsync(Guid entityId, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        const string sql = """
            SELECT fc.id, fc.event_id, fc.entity_id, fc.field,
                   fc.old_value, fc.new_value, fc.old_provider_id, fc.new_provider_id,
                   fc.confidence, fc.is_file_original
            FROM entity_field_changes fc
            INNER JOIN entity_events e ON e.id = fc.event_id
            WHERE fc.entity_id = @entity_id
            ORDER BY e.occurred_at DESC, fc.field ASC
            """;

        await using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@entity_id", entityId.ToString());
        return await ReadFieldChangesAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<EntityFieldChange>> GetFieldHistoryAsync(Guid entityId, string field, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        const string sql = """
            SELECT fc.id, fc.event_id, fc.entity_id, fc.field,
                   fc.old_value, fc.new_value, fc.old_provider_id, fc.new_provider_id,
                   fc.confidence, fc.is_file_original
            FROM entity_field_changes fc
            INNER JOIN entity_events e ON e.id = fc.event_id
            WHERE fc.entity_id = @entity_id AND fc.field = @field
            ORDER BY e.occurred_at DESC
            """;

        await using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@entity_id", entityId.ToString());
        cmd.Parameters.AddWithValue("@field", field);
        return await ReadFieldChangesAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<EntityFieldChange>> GetFileOriginalsForEventAsync(Guid eventId, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        const string sql = """
            SELECT id, event_id, entity_id, field,
                   old_value, new_value, old_provider_id, new_provider_id,
                   confidence, is_file_original
            FROM entity_field_changes
            WHERE event_id = @event_id AND is_file_original = 1
            ORDER BY field ASC
            """;

        await using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@event_id", eventId.ToString());
        return await ReadFieldChangesAsync(cmd, ct).ConfigureAwait(false);
    }

    // ── Queries for Vault List ─────────────────────────────────────────

    public async Task<IReadOnlyDictionary<Guid, EntityEvent>> GetLatestStage2EventsAsync(
        IReadOnlyList<Guid> entityIds, CancellationToken ct = default)
    {
        if (entityIds.Count == 0) return new Dictionary<Guid, EntityEvent>();

        using var conn = _db.CreateConnection();

        // SQLite doesn't support array parameters; use a temp table for efficiency.
        await using var createCmd = new SqliteCommand(
            "CREATE TEMP TABLE IF NOT EXISTS _tmp_entity_ids (id TEXT NOT NULL PRIMARY KEY)", conn);
        await createCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        await using var clearCmd = new SqliteCommand("DELETE FROM _tmp_entity_ids", conn);
        await clearCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        // Insert entity IDs into temp table.
        await using var tx = conn.BeginTransaction();
        foreach (var eid in entityIds)
        {
            await using var insertCmd = new SqliteCommand(
                "INSERT OR IGNORE INTO _tmp_entity_ids (id) VALUES (@id)", conn);
            insertCmd.Parameters.AddWithValue("@id", eid.ToString());
            await insertCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        tx.Commit();

        const string sql = """
            SELECT e.id, e.entity_id, e.entity_type, e.event_type, e.stage, e.trigger,
                   e.provider_id, e.provider_name, e.bridge_id_type, e.bridge_id_value,
                   e.resolved_qid, e.confidence,
                   e.score_title, e.score_author, e.score_year, e.score_format,
                   e.score_cross_field, e.score_cover_art, e.score_composite,
                   e.occurred_at, e.ingestion_run_id, e.detail
            FROM entity_events e
            INNER JOIN _tmp_entity_ids t ON t.id = e.entity_id
            WHERE e.stage = 2
              AND e.occurred_at = (
                  SELECT MAX(e2.occurred_at) FROM entity_events e2
                  WHERE e2.entity_id = e.entity_id AND e2.stage = 2
              )
            """;

        await using var cmd = new SqliteCommand(sql, conn);
        var events = await ReadEventsAsync(cmd, ct).ConfigureAwait(false);

        var dict = new Dictionary<Guid, EntityEvent>();
        foreach (var evt in events)
            dict.TryAdd(evt.EntityId, evt);
        return dict;
    }

    // ── Maintenance ────────────────────────────────────────────────────

    public async Task<int> CullOldEventsAsync(TimeSpan retention, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        var cutoff = DateTimeOffset.UtcNow.Subtract(retention).ToString("o");

        // Delete old events EXCEPT the most recent per entity per stage.
        // Field changes cascade via FK ON DELETE CASCADE.
        const string sql = """
            DELETE FROM entity_events
            WHERE occurred_at < @cutoff
              AND id NOT IN (
                  SELECT id FROM (
                      SELECT id, ROW_NUMBER() OVER (
                          PARTITION BY entity_id, stage ORDER BY occurred_at DESC
                      ) AS rn
                      FROM entity_events
                      WHERE stage IS NOT NULL
                  ) ranked
                  WHERE rn = 1
              )
            """;

        await using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@cutoff", cutoff);
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteByEntityAsync(Guid entityId, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        // Field changes cascade via FK ON DELETE CASCADE.
        const string sql = "DELETE FROM entity_events WHERE entity_id = @entity_id";
        await using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@entity_id", entityId.ToString());
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    // ── Private helpers ────────────────────────────────────────────────

    private static async Task<List<EntityEvent>> ReadEventsAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var results = new List<EntityEvent>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new EntityEvent
            {
                Id             = Guid.Parse(reader.GetString(0)),
                EntityId       = Guid.Parse(reader.GetString(1)),
                EntityType     = reader.GetString(2),
                EventType      = reader.GetString(3),
                Stage          = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                Trigger        = reader.GetString(5),
                ProviderId     = reader.IsDBNull(6) ? null : reader.GetString(6),
                ProviderName   = reader.IsDBNull(7) ? null : reader.GetString(7),
                BridgeIdType   = reader.IsDBNull(8) ? null : reader.GetString(8),
                BridgeIdValue  = reader.IsDBNull(9) ? null : reader.GetString(9),
                ResolvedQid    = reader.IsDBNull(10) ? null : reader.GetString(10),
                Confidence     = reader.IsDBNull(11) ? null : reader.GetDouble(11),
                ScoreTitle     = reader.IsDBNull(12) ? null : reader.GetDouble(12),
                ScoreAuthor    = reader.IsDBNull(13) ? null : reader.GetDouble(13),
                ScoreYear      = reader.IsDBNull(14) ? null : reader.GetDouble(14),
                ScoreFormat    = reader.IsDBNull(15) ? null : reader.GetDouble(15),
                ScoreCrossField = reader.IsDBNull(16) ? null : reader.GetDouble(16),
                ScoreCoverArt  = reader.IsDBNull(17) ? null : reader.GetDouble(17),
                ScoreComposite = reader.IsDBNull(18) ? null : reader.GetDouble(18),
                OccurredAt     = DateTimeOffset.Parse(reader.GetString(19)),
                IngestionRunId = reader.IsDBNull(20) ? null : Guid.Parse(reader.GetString(20)),
                Detail         = reader.IsDBNull(21) ? null : reader.GetString(21),
            });
        }
        return results;
    }

    private static async Task<List<EntityFieldChange>> ReadFieldChangesAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var results = new List<EntityFieldChange>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new EntityFieldChange
            {
                Id             = Guid.Parse(reader.GetString(0)),
                EventId        = Guid.Parse(reader.GetString(1)),
                EntityId       = Guid.Parse(reader.GetString(2)),
                Field          = reader.GetString(3),
                OldValue       = reader.IsDBNull(4) ? null : reader.GetString(4),
                NewValue       = reader.IsDBNull(5) ? null : reader.GetString(5),
                OldProviderId  = reader.IsDBNull(6) ? null : reader.GetString(6),
                NewProviderId  = reader.IsDBNull(7) ? null : reader.GetString(7),
                Confidence     = reader.IsDBNull(8) ? null : reader.GetDouble(8),
                IsFileOriginal = !reader.IsDBNull(9) && reader.GetInt32(9) == 1,
            });
        }
        return results;
    }
}
