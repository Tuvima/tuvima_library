using MediaEngine.Domain.Contracts;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite FTS5 full-text search index for works. Replaces in-memory re-ranking
/// with native database-level search, prefix matching, and BM25 ranking.
/// Supports cross-language search via original_title and alternate_titles columns
/// (e.g. "Amélie" matches "Le Fabuleux Destin d'Amélie Poulain" via unicode61 tokenizer).
/// </summary>
public sealed class SearchIndexRepository : ISearchIndexRepository
{
    private readonly IDatabaseConnection _db;

    public SearchIndexRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    /// <inheritdoc/>
    public async Task UpsertByEntityIdAsync(
        Guid entityId,
        string? title,
        string? originalTitle,
        string? alternateTitles,
        string? author,
        string? description,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(title)
            && string.IsNullOrWhiteSpace(originalTitle)
            && string.IsNullOrWhiteSpace(author))
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

        // FTS5 does not support UPSERT — delete then insert.
        using var del = conn.CreateCommand();
        del.CommandText = "DELETE FROM search_index WHERE entity_id = @workId;";
        del.Parameters.AddWithValue("@workId", workId);
        await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        using var ins = conn.CreateCommand();
        ins.CommandText = """
            INSERT INTO search_index (entity_id, title, original_title, alternate_titles, author, description)
            VALUES (@workId, @title, @originalTitle, @alternateTitles, @author, @description);
            """;
        ins.Parameters.AddWithValue("@workId",          workId);
        ins.Parameters.AddWithValue("@title",           (object?)title           ?? DBNull.Value);
        ins.Parameters.AddWithValue("@originalTitle",   (object?)originalTitle   ?? DBNull.Value);
        ins.Parameters.AddWithValue("@alternateTitles", (object?)alternateTitles ?? DBNull.Value);
        ins.Parameters.AddWithValue("@author",          (object?)author          ?? DBNull.Value);
        ins.Parameters.AddWithValue("@description",     (object?)description     ?? DBNull.Value);
        await ins.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Guid>> SearchAsync(string query, int limit = 50, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();

        // FTS5 prefix search: quote the term and append * for prefix matching.
        var escaped = query.Trim().Replace("\"", "\"\"");
        cmd.CommandText = """
            SELECT entity_id FROM search_index
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

    /// <inheritdoc/>
    public async Task RebuildAsync(CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();

        using var clear = conn.CreateCommand();
        clear.CommandText = "DELETE FROM search_index;";
        await clear.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        // Repopulate from canonical_values: title, original_title, description, and author.
        // alternate_titles are sourced from canonical_value_arrays (key='alternate_title')
        // and concatenated with a space separator for FTS5 tokenization.
        using var populate = conn.CreateCommand();
        populate.CommandText = """
            INSERT INTO search_index (entity_id, title, original_title, alternate_titles, author, description)
            SELECT
                w.id,
                MAX(CASE WHEN cv.key = 'title'          THEN cv.value END),
                MAX(CASE WHEN cv.key = 'original_title' THEN cv.value END),
                (SELECT GROUP_CONCAT(cva.value, ' ')
                 FROM canonical_value_arrays cva
                 JOIN editions e2 ON e2.work_id = w.id
                 JOIN media_assets ma2 ON ma2.edition_id = e2.id
                 WHERE cva.entity_id = ma2.id AND cva.key = 'alternate_title'),
                MAX(CASE WHEN cv.key = 'author'         THEN cv.value END),
                MAX(CASE WHEN cv.key = 'description'    THEN cv.value END)
            FROM works w
            LEFT JOIN editions e ON e.work_id = w.id
            LEFT JOIN media_assets ma ON ma.edition_id = e.id
            LEFT JOIN canonical_values cv ON cv.entity_id = ma.id
            WHERE cv.key IN ('title', 'original_title', 'author', 'description')
            GROUP BY w.id;
            """;
        await populate.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
