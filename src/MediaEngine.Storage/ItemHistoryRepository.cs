using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

public sealed class ItemHistoryRepository : IItemHistoryRepository
{
    private readonly IDatabaseConnection _db;

    public ItemHistoryRepository(IDatabaseConnection db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task AppendAsync(Guid entityId, string eventType, string label, string? detail = null, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            """
            INSERT INTO item_history (id, entity_id, occurred_at, event_type, label, detail)
            VALUES (@Id, @EntityId, @OccurredAt, @EventType, @Label, @Detail)
            """,
            new
            {
                Id = Guid.NewGuid().ToString(),
                EntityId = entityId.ToString(),
                OccurredAt = DateTimeOffset.UtcNow.ToString("o"),
                EventType = eventType,
                Label = label,
                Detail = detail
            });
    }

    public async Task<IReadOnlyList<ItemHistoryEntry>> GetHistoryAsync(Guid entityId, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<ItemHistoryRow>(
            """
            SELECT id, entity_id, occurred_at, event_type, label, detail
            FROM item_history
            WHERE entity_id = @EntityId
            ORDER BY occurred_at DESC
            """,
            new { EntityId = entityId.ToString() });

        return rows.Select(r => new ItemHistoryEntry
        {
            Id = r.id,
            EntityId = Guid.Parse(r.entity_id),
            OccurredAt = DateTimeOffset.Parse(r.occurred_at),
            EventType = r.event_type,
            Label = r.label,
            Detail = r.detail
        }).ToList();
    }

    private sealed record ItemHistoryRow(
        string id, string entity_id, string occurred_at,
        string event_type, string label, string? detail);
}
