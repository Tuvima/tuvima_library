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
    public async Task UpsertByEntityIdAsync(Guid entityId, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();

        // Phase 4 — lineage-aware refresh. The entityId may be either a
        // media_assets id (Self-scope writes) or a works id (Parent-scope
        // writes from the parent persist pass). Both resolve to the same
        // leaf work row.
        using var lookup = conn.CreateCommand();
        lookup.CommandText = """
            SELECT w.id                                AS work_id,
                   ma.id                               AS asset_id,
                   COALESCE(gp.id, p.id, w.id)         AS root_parent_id
            FROM works w
            LEFT JOIN works p          ON p.id  = w.parent_work_id
            LEFT JOIN works gp         ON gp.id = p.parent_work_id
            LEFT JOIN editions e       ON e.work_id = w.id
            LEFT JOIN media_assets ma  ON ma.edition_id = e.id
            WHERE w.id = @entityId
               OR ma.id = @entityId
            LIMIT 1
            """;
        lookup.Parameters.AddWithValue("@entityId", entityId.ToString());

        string? workId = null, assetId = null, rootParentId = null;
        using (var rdr = await lookup.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (await rdr.ReadAsync(ct).ConfigureAwait(false))
            {
                workId       = rdr.IsDBNull(0) ? null : rdr.GetString(0);
                assetId      = rdr.IsDBNull(1) ? null : rdr.GetString(1);
                rootParentId = rdr.IsDBNull(2) ? null : rdr.GetString(2);
            }
        }
        if (workId is null) return;

        // Self-scope reads (asset row).
        string? title = null, originalTitle = null;
        if (assetId is not null)
        {
            using var selfCmd = conn.CreateCommand();
            selfCmd.CommandText = """
                SELECT key, value FROM canonical_values
                WHERE entity_id = @assetId
                  AND key IN ('title', 'original_title')
                """;
            selfCmd.Parameters.AddWithValue("@assetId", assetId);
            using var sr = await selfCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await sr.ReadAsync(ct).ConfigureAwait(false))
            {
                var k = sr.GetString(0);
                var v = sr.IsDBNull(1) ? null : sr.GetString(1);
                if (k == "title")          title         = v;
                if (k == "original_title") originalTitle = v;
            }
        }

        // Parent-scope reads (topmost Work row).
        string? author = null, description = null;
        if (rootParentId is not null)
        {
            using var parentCmd = conn.CreateCommand();
            parentCmd.CommandText = """
                SELECT key, value FROM canonical_values
                WHERE entity_id = @parentId
                  AND key IN ('author', 'description')
                """;
            parentCmd.Parameters.AddWithValue("@parentId", rootParentId);
            using var pr = await parentCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await pr.ReadAsync(ct).ConfigureAwait(false))
            {
                var k = pr.GetString(0);
                var v = pr.IsDBNull(1) ? null : pr.GetString(1);
                if (k == "author")      author      = v;
                if (k == "description") description = v;
            }
        }

        // Alternate titles live in canonical_value_arrays. They are Self-scope
        // by default, so they live on the asset row.
        string? alternateTitles = null;
        if (assetId is not null)
        {
            using var altCmd = conn.CreateCommand();
            altCmd.CommandText = """
                SELECT GROUP_CONCAT(value, ' ')
                FROM canonical_value_arrays
                WHERE entity_id = @assetId AND key = 'alternate_title'
                """;
            altCmd.Parameters.AddWithValue("@assetId", assetId);
            var altObj = await altCmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            alternateTitles = altObj as string;
        }

        // Skip empty rows entirely.
        if (string.IsNullOrWhiteSpace(title)
            && string.IsNullOrWhiteSpace(originalTitle)
            && string.IsNullOrWhiteSpace(author))
            return;

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

        var trimmed = query.Trim();
        using var conn = _db.CreateConnection();

        // The trigram tokenizer requires at least 3 characters per token.
        // For short queries (< 3 chars) fall back to a LIKE scan on canonical_values
        // because the FTS5 trigram index cannot match them.
        if (trimmed.Length < 3)
        {
            // Phase 4 — title/original_title are Self (asset row), author is Parent
            // (topmost Work row). Search both targets via UNION.
            using var likeCmd = conn.CreateCommand();
            likeCmd.CommandText = """
                SELECT DISTINCT w.id
                FROM works w
                JOIN editions e ON e.work_id = w.id
                JOIN media_assets ma ON ma.edition_id = e.id
                JOIN canonical_values cv ON cv.entity_id = ma.id
                WHERE cv.key IN ('title', 'original_title')
                  AND cv.value LIKE @pattern
                UNION
                SELECT DISTINCT w.id
                FROM works w
                LEFT JOIN works p  ON p.id  = w.parent_work_id
                LEFT JOIN works gp ON gp.id = p.parent_work_id
                JOIN canonical_values cv
                  ON cv.entity_id = COALESCE(gp.id, p.id, w.id)
                WHERE cv.key = 'author'
                  AND cv.value LIKE @pattern
                LIMIT @limit
                """;
            likeCmd.Parameters.AddWithValue("@pattern", $"%{trimmed}%");
            likeCmd.Parameters.AddWithValue("@limit", limit);

            var likeResults = new List<Guid>();
            using var likeReader = await likeCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await likeReader.ReadAsync(ct).ConfigureAwait(false))
            {
                if (Guid.TryParse(likeReader.GetString(0), out var likeId))
                    likeResults.Add(likeId);
            }
            return likeResults;
        }

        // FTS5 trigram search: quote the phrase for exact substring matching.
        // The trigram tokenizer does not support prefix (*) or BM25 ranking —
        // omit the trailing * and ORDER BY rank clause.
        var escaped = trimmed.Replace("\"", "\"\"");
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT entity_id FROM search_index
            WHERE search_index MATCH @ftsQuery
            LIMIT @limit
            """;
        cmd.Parameters.AddWithValue("@ftsQuery", $"\"{escaped}\"");
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

        // Phase 4 — repopulate using lineage-aware sources:
        //   • title, original_title, alternate_titles → SELF (asset row)
        //   • author, description                     → PARENT (topmost Work)
        using var populate = conn.CreateCommand();
        populate.CommandText = """
            INSERT INTO search_index (entity_id, title, original_title, alternate_titles, author, description)
            SELECT
                w.id,
                (SELECT MAX(cv.value)
                 FROM canonical_values cv
                 JOIN media_assets ma2 ON ma2.id = cv.entity_id
                 JOIN editions e2 ON e2.id = ma2.edition_id
                 WHERE e2.work_id = w.id AND cv.key = 'title'),
                (SELECT MAX(cv.value)
                 FROM canonical_values cv
                 JOIN media_assets ma3 ON ma3.id = cv.entity_id
                 JOIN editions e3 ON e3.id = ma3.edition_id
                 WHERE e3.work_id = w.id AND cv.key = 'original_title'),
                (SELECT GROUP_CONCAT(cva.value, ' ')
                 FROM canonical_value_arrays cva
                 JOIN editions e4 ON e4.work_id = w.id
                 JOIN media_assets ma4 ON ma4.edition_id = e4.id
                 WHERE cva.entity_id = ma4.id AND cva.key = 'alternate_title'),
                (SELECT cv.value
                 FROM canonical_values cv
                 WHERE cv.entity_id = COALESCE(gp.id, p.id, w.id)
                   AND cv.key = 'author'
                 LIMIT 1),
                (SELECT cv.value
                 FROM canonical_values cv
                 WHERE cv.entity_id = COALESCE(gp.id, p.id, w.id)
                   AND cv.key = 'description'
                 LIMIT 1)
            FROM works w
            LEFT JOIN works p  ON p.id  = w.parent_work_id
            LEFT JOIN works gp ON gp.id = p.parent_work_id
            -- Only index leaf works that have at least one media asset.
            WHERE EXISTS (
                SELECT 1 FROM editions ex
                JOIN media_assets max ON max.edition_id = ex.id
                WHERE ex.work_id = w.id
            );
            """;
        await populate.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
