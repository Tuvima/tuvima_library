using Dapper;
using MediaEngine.Domain.Authorization;
using MediaEngine.Domain.Events;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

public sealed class ApplicationEventRepository(IDatabaseConnection database) : IApplicationEventRepository
{
    public Task<StoredApplicationEvent> AppendAsync(StoredApplicationEvent value, CancellationToken ct = default) =>
        database.ExecuteWriteAsync((connection, transaction, token) =>
        {
            token.ThrowIfCancellationRequested();
            return Insert(connection, transaction, value);
        }, ct);

    public Task<IReadOnlyList<StoredApplicationEvent>> ReadAfterAsync(long sequence, int limit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var connection = database.CreateConnection();
        var rows = connection.Query<Row>(new CommandDefinition(
            SelectSql + " WHERE sequence > @sequence ORDER BY sequence LIMIT @limit;",
            new { sequence, limit = Math.Clamp(limit, 1, 1000) }, cancellationToken: ct));
        return Task.FromResult<IReadOnlyList<StoredApplicationEvent>>(rows.Select(Map).ToArray());
    }

    public Task<long?> FindSequenceAsync(Guid eventId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var connection = database.CreateConnection();
        return Task.FromResult(connection.QuerySingleOrDefault<long?>(
            "SELECT sequence FROM application_events WHERE event_id=@eventId;", new { eventId }));
    }

    public Task<ApplicationEventBounds> GetBoundsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var connection = database.CreateConnection();
        var oldest = connection.QuerySingleOrDefault<Row>(SelectSql + " ORDER BY sequence LIMIT 1;");
        var latest = connection.QuerySingleOrDefault<Row>(SelectSql + " ORDER BY sequence DESC LIMIT 1;");
        return Task.FromResult(new ApplicationEventBounds(oldest?.Sequence, oldest?.EventId, latest?.Sequence, latest?.EventId));
    }

    public Task<int> PruneAsync(DateTimeOffset olderThan, int retainNewest, CancellationToken ct = default) =>
        database.ExecuteWriteAsync((connection, transaction, token) =>
        {
            token.ThrowIfCancellationRequested();
            return connection.Execute("""
                DELETE FROM application_events
                WHERE occurred_at < @olderThan
                  AND sequence < COALESCE((SELECT sequence FROM application_events ORDER BY sequence DESC LIMIT 1 OFFSET @retain), 0);
                """, new { olderThan = olderThan.ToString("O"), retain = Math.Clamp(retainNewest, 1, 1_000_000) - 1 }, transaction);
        }, ct);

    private const string SelectSql = """
        SELECT sequence,event_id AS EventId,event_type AS EventType,version,occurred_at AS OccurredAt,
               server_id AS ServerId,subject_type AS SubjectType,subject_id AS SubjectId,
               library_id AS LibraryId,profile_id AS ProfileId,feature_id AS FeatureId,payload_json AS PayloadJson
        FROM application_events
        """;

    internal static StoredApplicationEvent Insert(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        Microsoft.Data.Sqlite.SqliteTransaction transaction,
        StoredApplicationEvent value)
    {
        connection.Execute("""
            INSERT INTO application_events
                (event_id,event_type,version,occurred_at,server_id,subject_type,subject_id,library_id,profile_id,feature_id,payload_json)
            VALUES
                (@EventId,@EventType,@Version,@OccurredAt,@ServerId,@SubjectType,@SubjectId,@LibraryId,@ProfileId,@FeatureId,@PayloadJson);
            """, new
        {
            value.EventId,
            value.EventType,
            value.Version,
            OccurredAt = value.OccurredAt.ToString("O"),
            value.ServerId,
            SubjectType = value.Subject.Type,
            SubjectId = value.Subject.Id,
            value.Subject.LibraryId,
            value.Subject.ProfileId,
            FeatureId = value.Subject.FeatureId?.Value,
            value.PayloadJson,
        }, transaction);
        var sequence = connection.ExecuteScalar<long>("SELECT last_insert_rowid();", transaction: transaction);
        return value with { Sequence = sequence };
    }

    private sealed class Row
    {
        public long Sequence { get; set; }
        public Guid EventId { get; set; }
        public string EventType { get; set; } = "";
        public int Version { get; set; }
        public string OccurredAt { get; set; } = "";
        public string ServerId { get; set; } = "";
        public string SubjectType { get; set; } = "";
        public string SubjectId { get; set; } = "";
        public Guid? LibraryId { get; set; }
        public Guid? ProfileId { get; set; }
        public string? FeatureId { get; set; }
        public string PayloadJson { get; set; } = "{}";
    }

    private static StoredApplicationEvent Map(Row row) => new(
        row.Sequence, row.EventId, row.EventType, row.Version, DateTimeOffset.Parse(row.OccurredAt), row.ServerId,
        new(row.SubjectType, row.SubjectId, row.LibraryId, row.ProfileId,
            row.FeatureId is null ? null : new AccountFeatureId(row.FeatureId)), row.PayloadJson);
}
