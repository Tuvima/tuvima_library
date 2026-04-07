using System.Text.Json;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Storage.Services;

/// <summary>
/// Adds catalog rows for child entities Wikidata knows about but the
/// library doesn't yet own.
///
/// When the Wikidata enrichment chain finishes resolving an album, TV show,
/// season, or comic series, it produces a <c>child_entities_json</c> blob
/// describing every track, episode, or issue in the parent. This service
/// walks that JSON, matches each entry against the children already under
/// the parent (by ordinal where possible, by title otherwise), and inserts
/// a <see cref="WorkKind.Catalog"/> row for any unmatched entries.
///
/// Catalog rows have <c>is_catalog_only = 1</c> and no Edition or
/// MediaAsset. They become real owned children when their files are later
/// ingested — the <c>HierarchyResolver</c> finds them by ordinal under the
/// same parent and calls <see cref="IWorkRepository.PromoteCatalogToOwnedAsync"/>.
///
/// This service is invoked from the Wikidata enrichment workers (deferred
/// to Phase 3b — the call site requires an asset→work routing decision
/// that's tracked separately).
/// </summary>
public sealed class CatalogUpsertService
{
    private readonly IWorkRepository _works;
    private readonly ILogger<CatalogUpsertService>? _logger;

    public CatalogUpsertService(
        IWorkRepository works,
        ILogger<CatalogUpsertService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(works);
        _works = works;
        _logger = logger;
    }

    /// <summary>
    /// Reads the Wikidata <c>child_entities_json</c> payload and inserts a
    /// catalog Work for every child not already present under
    /// <paramref name="parentWorkId"/>. Existing children (matched by
    /// ordinal first, then title) are left alone.
    /// </summary>
    /// <returns>The number of new catalog rows inserted.</returns>
    public async Task<int> UpsertChildrenAsync(
        Guid parentWorkId,
        MediaType childMediaType,
        string childEntitiesJson,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(childEntitiesJson)) return 0;

        ChildEntityPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<ChildEntityPayload>(childEntitiesJson);
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex,
                "CatalogUpsert: malformed child_entities_json for parent {ParentWorkId}",
                parentWorkId);
            return 0;
        }

        if (payload is null) return 0;

        // Pick the right child collection for this media type. The Wikidata
        // adapter writes "tracks" for music, "episodes" for TV (within
        // seasons), and "issues" for comics — keys mirror the property
        // names in DiscoverChildEntitiesAsync.
        var children = childMediaType switch
        {
            MediaType.Music  => payload.Tracks,
            MediaType.TV     => payload.Episodes,
            MediaType.Comics => payload.Issues,
            _                => null,
        };

        if (children is null || children.Count == 0)
            return 0;

        var inserted = 0;
        foreach (var child in children)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(child.Title)) continue;

            // Match by ordinal first — most reliable.
            Guid? existing = null;
            if (child.Ordinal is { } ordinal)
                existing = await _works.FindChildByOrdinalAsync(parentWorkId, ordinal, ct);

            // Fall back to title.
            existing ??= await _works.FindChildByTitleAsync(parentWorkId, child.Title, ct);

            if (existing is not null)
            {
                // Already present (owned or already a catalog row).
                // We deliberately do NOT touch the row — its canonical values
                // and external_identifiers are managed by the enrichment
                // chain, not by this service.
                continue;
            }

            var ids = BuildExternalIds(child);
            await _works.InsertCatalogChildAsync(
                childMediaType, parentWorkId, child.Ordinal, ids, ct);
            inserted++;
        }

        if (inserted > 0)
            _logger?.LogInformation(
                "CatalogUpsert: added {Count} catalog {MediaType} child(ren) under parent {ParentWorkId}",
                inserted, childMediaType, parentWorkId);

        return inserted;
    }

    private static IReadOnlyDictionary<string, string>? BuildExternalIds(ChildEntity child)
    {
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(child.Qid))     ids["wikidata_qid"] = child.Qid;
        if (!string.IsNullOrWhiteSpace(child.ImdbId))  ids["imdb_id"]     = child.ImdbId;
        if (!string.IsNullOrWhiteSpace(child.TmdbId))  ids["tmdb_id"]     = child.TmdbId;
        return ids.Count == 0 ? null : ids;
    }

    // ── Payload shapes mirror the JSON written by ReconciliationAdapter ───────

    private sealed class ChildEntityPayload
    {
        public List<ChildEntity>? Tracks   { get; set; }
        public List<ChildEntity>? Episodes { get; set; }
        public List<ChildEntity>? Issues   { get; set; }
    }

    private sealed class ChildEntity
    {
        public string? Title   { get; set; }
        public int?    Ordinal { get; set; }
        public string? Qid     { get; set; }
        public string? ImdbId  { get; set; }
        public string? TmdbId  { get; set; }
    }
}
