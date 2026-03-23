using MediaEngine.Domain.Contracts;
using MediaEngine.Storage.Contracts;
using Microsoft.Data.Sqlite;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite FTS5 full-text search index for works. Replaces in-memory FuzzySharp
/// re-ranking with native database-level search, prefix matching, and BM25 ranking.
/// </summary>
public sealed class SearchIndexRepository : ISearchIndexRepository
{
    private readonly IDatabaseConnection _db;

    public SearchIndexRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    public async Task UpsertByEntityIdAsync(Guid entityId, string? title, string? author, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(author))
            return;

        using var conn = _db.CreateConnection();

        // Resolve work_id from media_asset entity_id.
        using var lookup = conn.CreateCommand();
        lookup.CommandText = """
            SELECT w.id FROM works w
            JOIN editions e ON e.work_id = w.id
            JOIN media_assets ma ON ma.edition_id = e.id
            WHERE ma.id = @entityId
            LIMIT 1
            """;
        lookup.Parameters.AddWithValue("@entityId", entityId.ToString());
        var workIdObj = await lookup.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (workIdObj is null) return;

        var workId = workIdObj.ToString()!;

        // Delete existing entry for this work, then insert fresh.
        using var upsert = conn.CreateCommand();
        upsert.CommandText = """
            DELETE FROM search_index WHERE work_id = @workId;
            INSERT INTO search_index (work_id, title, author)
            VALUES (@workId, @title, @author);
            """;
        upsert.Parameters.AddWithValue("@workId", workId);
        upsert.Parameters.AddWithValue("@title", (object?)title ?? DBNull.Value);
        upsert.Parameters.AddWithValue("@author", (object?)author ?? DBNull.Value);
        await upsert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Guid>> SearchAsync(string query, int limit = 50, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();

        // FTS5 prefix search: wrap in quotes, append *.
        var escaped = query.Trim().Replace("\"", "\"\"");
        cmd.CommandText = """
            SELECT work_id FROM search_index
            WHERE search_index MATCH @ftsQuery
            ORDER BY bm25(search_index)
            LIMIT @limit
            """;
        cmd.Parameters.AddWithValue("@ftsQuery", $"\"{escaped}\"*");
        cmd.Parameters.AddWithValue("@limit", limit);

        var results = new List<Guid>();
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (Guid.TryParse(reader.GetString(0), out var id))
                results.Add(id);
        }
        return results;
    }

    public async Task RebuildAsync(CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();

        using var clear = conn.CreateCommand();
        clear.CommandText = "DELETE FROM search_index;";
        await clear.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        using var populate = conn.CreateCommand();
        populate.CommandText = """
            INSERT INTO search_index (work_id, title, author)
            SELECT
                w.id,
                MAX(CASE WHEN cv.key = 'title' THEN cv.value END),
                MAX(CASE WHEN cv.key = 'author' THEN cv.value END)
            FROM works w
            LEFT JOIN editions e ON e.work_id = w.id
            LEFT JOIN media_assets ma ON ma.edition_id = e.id
            LEFT JOIN canonical_values cv ON cv.entity_id = ma.id
            WHERE cv.key IN ('title', 'author')
            GROUP BY w.id;
            """;
        await populate.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
