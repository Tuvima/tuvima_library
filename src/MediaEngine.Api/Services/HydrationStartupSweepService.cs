using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services;

/// <summary>
/// One-shot background service that runs 15 seconds after the Engine starts.
///
/// Queries the database for media assets that were never successfully hydrated
/// (no real <c>wikidata_qid</c> canonical value) and re-enqueues them into the
/// hydration pipeline. This recovers items that were lost when the Engine was
/// killed mid-batch during a previous run.
///
/// Items are excluded if they have a pending <c>LanguageMismatch</c>
/// review item — those require user action first.
/// </summary>
public sealed class HydrationStartupSweepService : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);

    private readonly IDatabaseConnection        _db;
    private readonly IHydrationPipelineService  _pipeline;
    private readonly ILogger<HydrationStartupSweepService> _logger;

    public HydrationStartupSweepService(
        IDatabaseConnection        db,
        IHydrationPipelineService  pipeline,
        ILogger<HydrationStartupSweepService> logger)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(logger);

        _db       = db;
        _pipeline = pipeline;
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "HydrationStartupSweepService: waiting {Seconds}s before sweep",
            StartupDelay.TotalSeconds);

        await Task.Delay(StartupDelay, stoppingToken);

        if (stoppingToken.IsCancellationRequested) return;

        try
        {
            await RunSweepAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Shutdown during sweep — no action needed.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HydrationStartupSweepService: sweep failed unexpectedly");
        }

        // Service is done — it only runs once on startup.
    }

    // ── Sweep logic ───────────────────────────────────────────────────────────

    private async Task RunSweepAsync(CancellationToken ct)
    {
        var items = LoadUnhydratedItems();

        _logger.LogInformation(
            "Startup sweep: found {Count} un-hydrated items, re-enqueueing for hydration",
            items.Count);

        if (items.Count == 0) return;

        foreach (var (assetId, mediaTypeStr, title, author, isbn) in items)
        {
            if (ct.IsCancellationRequested) break;

            if (!Enum.TryParse<MediaType>(mediaTypeStr, ignoreCase: true, out var mediaType))
                mediaType = MediaType.Unknown;

            var hints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(title))  hints["title"]  = title;
            if (!string.IsNullOrWhiteSpace(author)) hints["author"] = author;
            if (!string.IsNullOrWhiteSpace(isbn))   hints["isbn"]   = isbn;

            var request = new HarvestRequest
            {
                EntityId   = assetId,
                EntityType = EntityType.MediaAsset,
                MediaType  = mediaType,
                Hints      = hints,
                Pass       = HydrationPass.Quick,
            };

            await _pipeline.EnqueueAsync(request, ct).ConfigureAwait(false);

            _logger.LogDebug(
                "Startup sweep: re-enqueued entity {Id} (type={MediaType}, title=\"{Title}\")",
                assetId, mediaType, title ?? "(unknown)");
        }

        _logger.LogInformation(
            "Startup sweep: finished re-enqueueing {Count} items",
            items.Count);
    }

    // ── SQL helper ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns media assets that have no real <c>wikidata_qid</c> canonical value
    /// and no blocking review items (LanguageMismatch).
    /// </summary>
    private List<(Guid AssetId, string MediaType, string? Title, string? Author, string? Isbn)>
        LoadUnhydratedItems()
    {
        var results = new List<(Guid, string, string?, string?, string?)>();

        using var conn = _db.CreateConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT ma.id, w.media_type,
                   MAX(CASE WHEN cv.key = 'title'  THEN cv.value END) AS title,
                   MAX(CASE WHEN cv.key = 'author' THEN cv.value END) AS author,
                   MAX(CASE WHEN cv.key = 'isbn'   THEN cv.value END) AS isbn
            FROM media_assets ma
            JOIN editions e ON e.id = ma.edition_id
            JOIN works w    ON w.id = e.work_id
            LEFT JOIN canonical_values cv ON cv.entity_id = ma.id
            WHERE NOT EXISTS (
                SELECT 1 FROM canonical_values cv2
                WHERE cv2.entity_id = ma.id
                  AND cv2.key = 'wikidata_qid'
                  AND cv2.value IS NOT NULL
                  AND cv2.value != ''
                  AND cv2.value NOT LIKE 'NF%'
            )
            AND NOT EXISTS (
                SELECT 1 FROM review_queue rq
                WHERE rq.entity_id = ma.id
                  AND rq.status = 'Pending'
                  AND rq.trigger IN ('LanguageMismatch')
            )
            GROUP BY ma.id, w.media_type
            """;

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (!Guid.TryParse(reader.GetString(0), out var assetId)) continue;
            var mediaType = reader.IsDBNull(1) ? "Unknown" : reader.GetString(1);
            var title     = reader.IsDBNull(2) ? null : reader.GetString(2);
            var author    = reader.IsDBNull(3) ? null : reader.GetString(3);
            var isbn      = reader.IsDBNull(4) ? null : reader.GetString(4);
            results.Add((assetId, mediaType, title, author, isbn));
        }

        return results;
    }
}
