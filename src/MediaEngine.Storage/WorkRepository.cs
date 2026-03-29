using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IWorkRepository"/>.
///
/// Used during ingestion to detect whether a Work already exists for a given
/// title + author + media type, enabling new files for the same title to be
/// added as Editions under the existing Work rather than creating duplicate Works.
///
/// The lookup traverses: canonical_values → media_assets → editions → works,
/// because canonical_values.entity_id maps to media_assets.id.
/// </summary>
public sealed class WorkRepository : IWorkRepository
{
    private readonly IDatabaseConnection _db;

    public WorkRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    /// <inheritdoc/>
    public Task<WorkMatch?> FindByTitleAuthorAsync(
        string title,
        string? author,
        string mediaType,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();

        // Join canonical_values through media_assets and editions to reach works.
        // Two canonical_values rows per asset are needed: one for 'title', one
        // for 'author'. We use a self-join on cv_title / cv_author (LEFT JOIN so
        // works without an author claim still match when author is null).
        // When @author is provided, the author claim must also match.
        const string sql = """
            SELECT w.id          AS WorkId,
                   w.wikidata_qid AS WikidataQid
            FROM   works w
            INNER JOIN editions e   ON e.work_id     = w.id
            INNER JOIN media_assets ma ON ma.edition_id = e.id
            INNER JOIN canonical_values cv_title
                    ON cv_title.entity_id = ma.id AND cv_title.key = 'title'
            LEFT  JOIN canonical_values cv_author
                    ON cv_author.entity_id = ma.id AND cv_author.key = 'author'
            WHERE  w.media_type = @mediaType
              AND  cv_title.value = @title COLLATE NOCASE
              AND  (@author IS NULL OR @author = '' OR cv_author.value = @author COLLATE NOCASE)
            LIMIT  1;
            """;

        var match = conn.QueryFirstOrDefault<WorkMatchRow>(
            sql, new { title, author = author ?? string.Empty, mediaType });

        WorkMatch? result = match is null
            ? null
            : new WorkMatch(Guid.Parse(match.WorkId), match.WikidataQid);

        return Task.FromResult(result);
    }

    // Internal Dapper projection row — avoids exposing mutable types to Dapper.
    private sealed class WorkMatchRow
    {
        public string WorkId      { get; init; } = string.Empty;
        public string? WikidataQid { get; init; }
    }
}
