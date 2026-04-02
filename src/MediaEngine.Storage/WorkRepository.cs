using Dapper;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Storage.Contracts;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IWorkRepository"/>.
///
/// Used during ingestion to detect whether a Work already exists for a given
/// title + author + media type, enabling new files for the same title to be
/// added as Editions under the existing Work rather than creating duplicate Works.
///
/// Primary lookup traverses: canonical_values → media_assets → editions → works,
/// because canonical_values.entity_id maps to media_assets.id.
///
/// Fallback lookup traverses raw metadata_claims from local file processors,
/// catching the race condition where multiple files arrive before canonical_values
/// have been populated by the hydration pipeline.
/// </summary>
public sealed class WorkRepository : IWorkRepository
{
    private readonly IDatabaseConnection _db;
    private readonly ILogger<WorkRepository>? _logger;

    public WorkRepository(IDatabaseConnection db, ILogger<WorkRepository>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
        _logger = logger;
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

        // ── Primary lookup: canonical_values ─────────────────────────────────
        // Join canonical_values through media_assets and editions to reach works.
        // Two canonical_values rows per asset are needed: one for 'title', one
        // for 'author'. We use a self-join on cv_title / cv_author (LEFT JOIN so
        // works without an author claim still match when author is null).
        // When @author is provided, the author claim must also match.
        const string primarySql = """
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

        var primaryMatch = conn.QueryFirstOrDefault<WorkMatchRow>(
            primarySql, new { title, author = author ?? string.Empty, mediaType });

        if (primaryMatch is not null)
        {
            return Task.FromResult<WorkMatch?>(
                new WorkMatch(Guid.Parse(primaryMatch.WorkId), primaryMatch.WikidataQid));
        }

        // ── Fallback lookup: raw metadata_claims from file processors ─────────
        // canonical_values are only populated after Stage 1 of the hydration
        // pipeline. When several files for the same Work arrive in quick
        // succession, each ingestion thread finds empty canonical_values and
        // creates a duplicate Work. This fallback checks the raw claims emitted
        // by local file processors (LocalProcessor + LibraryScanner) before
        // that race window closes.
        const string fallbackSql = """
            SELECT w.id          AS WorkId,
                   w.wikidata_qid AS WikidataQid
            FROM   works w
            INNER JOIN editions e       ON e.work_id      = w.id
            INNER JOIN media_assets ma  ON ma.edition_id  = e.id
            INNER JOIN metadata_claims mc_title
                    ON mc_title.entity_id  = ma.id
                   AND mc_title.claim_key  = 'title'
                   AND mc_title.provider_id IN (@localProcessor, @libraryScanner)
            LEFT  JOIN metadata_claims mc_author
                    ON mc_author.entity_id  = ma.id
                   AND mc_author.claim_key  = 'author'
                   AND mc_author.provider_id IN (@localProcessor, @libraryScanner)
            WHERE  w.media_type = @mediaType
              AND  mc_title.claim_value = @title COLLATE NOCASE
              AND  (@author IS NULL OR @author = '' OR mc_author.claim_value = @author COLLATE NOCASE)
            LIMIT  1;
            """;

        var fallbackMatch = conn.QueryFirstOrDefault<WorkMatchRow>(
            fallbackSql,
            new
            {
                title,
                author           = author ?? string.Empty,
                mediaType,
                localProcessor   = WellKnownProviders.LocalProcessor.ToString(),
                libraryScanner   = WellKnownProviders.LibraryScanner.ToString(),
            });

        if (fallbackMatch is not null)
        {
            var workId = Guid.Parse(fallbackMatch.WorkId);
            _logger?.LogInformation(
                "Work dedup fallback: matched '{Title}' by '{Author}' to existing Work {WorkId} via raw claims",
                title, author, workId);

            return Task.FromResult<WorkMatch?>(
                new WorkMatch(workId, fallbackMatch.WikidataQid));
        }

        return Task.FromResult<WorkMatch?>(null);
    }

    // Internal Dapper projection row — avoids exposing mutable types to Dapper.
    private sealed class WorkMatchRow
    {
        public string WorkId      { get; init; } = string.Empty;
        public string? WikidataQid { get; init; }
    }
}
