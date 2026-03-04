using Microsoft.Data.Sqlite;
using Tanaste.Domain.Aggregates;
using Tanaste.Domain.Entities;
using Tanaste.Domain.Enums;
using Tanaste.Storage.Contracts;

namespace Tanaste.Storage;

/// <summary>
/// ORM-less SQLite implementation of <see cref="IHubRepository"/>.
/// Loads all hubs with their child Works and each Work's CanonicalValues
/// using two sequential queries (no N+1) — same pattern as
/// <see cref="MediaAssetRepository"/>.
/// </summary>
public sealed class HubRepository : IHubRepository
{
    private readonly IDatabaseConnection _db;

    public HubRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<Hub>> GetAllAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var conn  = _db.Open();
        var hubs  = new Dictionary<Guid, Hub>();
        var works = new Dictionary<Guid, Work>();

        // ── Query A: all hubs LEFT JOIN their works ───────────────────────────
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT h.id, h.universe_id, h.display_name, h.created_at,
                       h.universe_status,
                       w.id, w.media_type, w.sequence_index,
                       w.universe_mismatch, w.universe_mismatch_at
                FROM   hubs h
                LEFT JOIN works w ON w.hub_id = h.id
                ORDER  BY h.created_at, w.id;
                """;

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var hubId = Guid.Parse(reader.GetString(0));
                if (!hubs.TryGetValue(hubId, out var hub))
                {
                    hub = new Hub
                    {
                        Id             = hubId,
                        UniverseId     = reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
                        DisplayName    = reader.IsDBNull(2) ? null : reader.GetString(2),
                        CreatedAt      = DateTimeOffset.Parse(reader.GetString(3)),
                        UniverseStatus = reader.IsDBNull(4) ? "Unknown" : reader.GetString(4),
                    };
                    hubs[hubId] = hub;
                }

                // LEFT JOIN: work columns are NULL when the hub has no works.
                if (!reader.IsDBNull(5))
                {
                    var workId = Guid.Parse(reader.GetString(5));
                    if (!works.ContainsKey(workId))
                    {
                        var work = new Work
                        {
                            Id                 = workId,
                            HubId              = hubId,
                            MediaType          = Enum.Parse<MediaType>(reader.GetString(6), ignoreCase: true),
                            SequenceIndex      = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                            UniverseMismatch   = !reader.IsDBNull(8) && reader.GetInt32(8) == 1,
                            UniverseMismatchAt = reader.IsDBNull(9) ? null : DateTimeOffset.Parse(reader.GetString(9)),
                        };
                        works[workId] = work;
                        hub.Works.Add(work);
                    }
                }
            }
        }

        // ── Query B: canonical values for all loaded works ────────────────────
        if (works.Count > 0)
        {
            var workIds    = works.Keys.ToList();
            var paramNames = workIds.Select((_, i) => $"@p{i}").ToList();

            using var cmd2 = conn.CreateCommand();
            cmd2.CommandText = $"""
                SELECT entity_id, key, value, last_scored_at
                FROM   canonical_values
                WHERE  entity_id IN ({string.Join(", ", paramNames)});
                """;

            for (int i = 0; i < workIds.Count; i++)
                cmd2.Parameters.AddWithValue($"@p{i}", workIds[i].ToString());

            using var reader2 = cmd2.ExecuteReader();
            while (reader2.Read())
            {
                var entityId = Guid.Parse(reader2.GetString(0));
                if (works.TryGetValue(entityId, out var work))
                {
                    work.CanonicalValues.Add(new CanonicalValue
                    {
                        EntityId     = entityId,
                        Key          = reader2.GetString(1),
                        Value        = reader2.GetString(2),
                        LastScoredAt = DateTimeOffset.Parse(reader2.GetString(3)),
                    });
                }
            }
        }

        IReadOnlyList<Hub> result = hubs.Values.ToList();
        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<Hub?> FindByDisplayNameAsync(string displayName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, universe_id, display_name, created_at, universe_status
            FROM   hubs
            WHERE  LOWER(display_name) = LOWER(@name)
            LIMIT  1;
            """;
        cmd.Parameters.AddWithValue("@name", displayName);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return Task.FromResult<Hub?>(null);

        var hub = new Hub
        {
            Id             = Guid.Parse(reader.GetString(0)),
            UniverseId     = reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
            DisplayName    = reader.IsDBNull(2) ? null : reader.GetString(2),
            CreatedAt      = DateTimeOffset.Parse(reader.GetString(3)),
            UniverseStatus = reader.IsDBNull(4) ? "Unknown" : reader.GetString(4),
        };

        return Task.FromResult<Hub?>(hub);
    }

    /// <inheritdoc/>
    public Task<Guid> UpsertAsync(Hub hub, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var conn = _db.Open();

        // INSERT OR IGNORE ensures idempotency for new hubs.
        // UPDATE sets display_name on every call so the latest ingested name wins.
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO hubs(id, universe_id, display_name, created_at, universe_status)
                VALUES (@id, @uid, @dn, @ca, @us);
            UPDATE hubs SET display_name = @dn, universe_status = @us WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@id",  hub.Id.ToString());
        cmd.Parameters.AddWithValue("@uid", hub.UniverseId.HasValue
            ? hub.UniverseId.Value.ToString()
            : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@dn",  hub.DisplayName ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ca",  hub.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@us",  hub.UniverseStatus ?? "Unknown");
        cmd.ExecuteNonQuery();

        return Task.FromResult(hub.Id);
    }
}
