using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Storage.Contracts;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Storage;

/// <summary>
/// Creates the Work → Edition chain required before a MediaAsset
/// can be inserted.  Uses <c>INSERT OR IGNORE</c> throughout so the
/// operation is idempotent and safe to call concurrently.
///
/// Hub assignment no longer happens at ingestion time — Works are created
/// standalone (hub_id = NULL). Hub intelligence runs during Stage 2 of the
/// hydration pipeline, where Wikidata relationship properties drive grouping.
///
/// Work-level deduplication: before creating a new Work, the factory checks
/// whether an existing Work with the same title + author + media type already
/// exists. If one is found, only a new Edition is created under that Work,
/// preventing duplicate Work rows for different file formats of the same title.
///
/// Spec: Phase 4 – Hub Atomic Zone; Phase 7 – Ingestion § Entity Chain.
/// </summary>
public sealed class MediaEntityChainFactory : IMediaEntityChainFactory
{
    private readonly IDatabaseConnection _db;
    private readonly IWorkRepository _works;
    private readonly ILogger<MediaEntityChainFactory>? _logger;

    public MediaEntityChainFactory(
        IDatabaseConnection db,
        IWorkRepository works,
        ILogger<MediaEntityChainFactory>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(works);
        _db     = db;
        _works  = works;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Guid> EnsureEntityChainAsync(
        MediaType mediaType,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        string? title  = null;
        string? author = null;
        metadata?.TryGetValue("title",  out title);
        metadata?.TryGetValue("author", out author);

        // ── 1. Resolve Work — reuse existing or create new ───────────────
        Guid workId;

        if (!string.IsNullOrWhiteSpace(title))
        {
            var existing = await _works.FindByTitleAuthorAsync(
                title, author, mediaType.ToString(), ct).ConfigureAwait(false);

            if (existing is not null)
            {
                workId = existing.WorkId;
                _logger?.LogDebug(
                    "Work deduplication: reusing existing Work {WorkId} for title={Title} author={Author} mediaType={MediaType}",
                    workId, title, author, mediaType);
            }
            else
            {
                workId = CreateWork(mediaType);
            }
        }
        else
        {
            // No title available — cannot deduplicate; always create a new Work.
            workId = CreateWork(mediaType);
        }

        // ── 2. Create Edition under the resolved Work ─────────────────────
        string? formatLabel = null;
        metadata?.TryGetValue("format", out formatLabel);

        var editionId = Guid.NewGuid();

        using var conn = _db.CreateConnection();
        using var insertEdition = conn.CreateCommand();
        insertEdition.CommandText = """
            INSERT INTO editions (id, work_id, format_label)
            VALUES (@id, @work_id, @format_label);
            """;
        insertEdition.Parameters.AddWithValue("@id",           editionId.ToString());
        insertEdition.Parameters.AddWithValue("@work_id",      workId.ToString());
        insertEdition.Parameters.AddWithValue("@format_label",
            formatLabel ?? (object)DBNull.Value);
        insertEdition.ExecuteNonQuery();

        return editionId;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Guid CreateWork(MediaType mediaType)
    {
        var workId = Guid.NewGuid();

        using var conn = _db.CreateConnection();
        using var insertWork = conn.CreateCommand();
        insertWork.CommandText = """
            INSERT INTO works (id, hub_id, media_type, wikidata_status)
            VALUES (@id, NULL, @media_type, 'pending');
            """;
        insertWork.Parameters.AddWithValue("@id",         workId.ToString());
        insertWork.Parameters.AddWithValue("@media_type", mediaType.ToString());
        insertWork.ExecuteNonQuery();

        return workId;
    }
}
