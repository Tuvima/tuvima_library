using System.Text.Json;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
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
    private readonly IHubRepository _hubs;
    private readonly ILogger<MediaEntityChainFactory>? _logger;

    public MediaEntityChainFactory(
        IDatabaseConnection db,
        IWorkRepository works,
        IHubRepository hubs,
        ILogger<MediaEntityChainFactory>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(works);
        ArgumentNullException.ThrowIfNull(hubs);
        _db     = db;
        _works  = works;
        _hubs   = hubs;
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

        // ── 1b. Ensure content group hub ────────────────────────────────
        await EnsureContentGroupAsync(workId, mediaType, metadata, ct).ConfigureAwait(false);

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

    private async Task EnsureContentGroupAsync(
        Guid workId, MediaType mediaType,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken ct)
    {
        if (metadata is null) return;

        // Determine grouping field based on media type
        string? groupField = mediaType switch
        {
            MediaType.Music => "album",
            MediaType.TV => "show_name",
            MediaType.Books or MediaType.Comics => "series",
            MediaType.Audiobooks => "series",
            MediaType.Podcasts => "show_name",
            _ => null,
        };

        if (groupField is null) return;

        // Check if the metadata has the grouping value
        if (!metadata.TryGetValue(groupField, out var groupValue) || string.IsNullOrWhiteSpace(groupValue))
            return;

        // Build predicates for this content group
        var predicates = new List<HubRulePredicate>
        {
            new() { Field = "media_type", Op = "eq", Value = mediaType.ToString() },
            new() { Field = groupField, Op = "eq", Value = groupValue },
        };

        // Add artist for music albums to distinguish same-named albums
        if (mediaType == MediaType.Music && metadata.TryGetValue("artist", out var artist) && !string.IsNullOrWhiteSpace(artist))
        {
            predicates.Add(new() { Field = "artist", Op = "eq", Value = artist });
        }

        var ruleHash = HubRuleEvaluator.ComputeRuleHash(predicates);

        // Check if content group already exists
        var existing = await _hubs.FindByRuleHashAsync(ruleHash, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            // Assign work to existing hub
            await _hubs.AssignWorkToHubAsync(workId, existing.Id, ct).ConfigureAwait(false);
            return;
        }

        // Create new content group hub
        var displayName = mediaType switch
        {
            MediaType.Music => metadata.TryGetValue("artist", out var a) && !string.IsNullOrWhiteSpace(a)
                ? $"{groupValue} — {a}" : groupValue,
            _ => groupValue,
        };

        var hubId = Guid.NewGuid();
        var ruleJson = JsonSerializer.Serialize(predicates);

        await _hubs.UpsertAsync(new Hub
        {
            Id = hubId,
            DisplayName = displayName,
            HubType = "ContentGroup",
            Description = $"{mediaType} content group: {groupValue}",
            Scope = "library",
            IsEnabled = true,
            MinItems = 0,
            RuleJson = ruleJson,
            Resolution = "materialized",
            RuleHash = ruleHash,
            GroupByField = groupField,
            MatchMode = "all",
            SortField = mediaType == MediaType.Music ? "track_number" : mediaType == MediaType.TV ? "episode" : "title",
            SortDirection = "asc",
            LiveUpdating = false,
            CreatedAt = DateTimeOffset.UtcNow,
        }, ct).ConfigureAwait(false);

        // Assign work to the new hub
        await _hubs.AssignWorkToHubAsync(workId, hubId, ct).ConfigureAwait(false);

        _logger?.LogInformation(
            "Created ContentGroup hub {HubId} ({DisplayName}) for {MediaType} work {WorkId}",
            hubId, displayName, mediaType, workId);
    }

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
