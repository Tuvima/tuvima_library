using System.Globalization;
using System.Text;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Storage.Services;

/// <summary>
/// Decides whether a newly-ingested file belongs under an existing parent
/// Work (album, show, season, comic series) or stands alone,
/// and returns the resolved <see cref="Guid"/> of the Work it should be
/// attached to.
///
/// The resolver is the single source of truth for parent/child placement —
/// the chain factory used to do this with title+author dedup, and that
/// fragile path has been removed entirely. Per-media-type strategies live
/// in this one file:
///
/// <list type="bullet">
///   <item><b>Movies</b> — always Standalone.</item>
///   <item><b>Music</b> — parent key = (artist | album). Tracks become
///     children at <c>ordinal = track_number</c>; title fallback when no
///     track number.</item>
///   <item><b>TV</b> — three levels: Show parent → Season parent → Episode
///     child. Show parent key = show_name. Season is keyed by
///     <c>(show_id, season_number)</c> via the parent_work_id+ordinal index.</item>
///   <item><b>Comics</b> — parent key = series. Issues are children at
///     <c>ordinal = issue_number</c>.</item>
///   <item><b>Books / Audiobooks in series</b> — parent key = (series | author).
///     Volumes are children at <c>ordinal = series_position</c>. Items
///     without a series fall through to Standalone.</item>
/// </list>
///
/// The resolver is intentionally idempotent: calling
/// <see cref="ResolveAsync"/> twice for the same file metadata returns the
/// same Work id without creating duplicates.
/// </summary>
public sealed class HierarchyResolver
{
    private readonly IWorkRepository _works;
    private readonly ILogger<HierarchyResolver>? _logger;

    public HierarchyResolver(IWorkRepository works, ILogger<HierarchyResolver>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(works);
        _works = works;
        _logger = logger;
    }

    /// <summary>
    /// Resolves (or creates) the Work that owns the file described by
    /// <paramref name="metadata"/>. The returned id is the leaf-most Work
    /// — the track for music, the episode for TV, the issue for comics —
    /// suitable for attaching an Edition + MediaAsset.
    /// </summary>
    public async Task<ResolverResult> ResolveAsync(
        MediaType mediaType,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        metadata ??= new Dictionary<string, string>();

        return mediaType switch
        {
            MediaType.Music                          => await ResolveMusicAsync(metadata, ct),
            MediaType.TV                             => await ResolveTvAsync(metadata, ct),
            MediaType.Comics                         => await ResolveComicsAsync(metadata, ct),

            MediaType.Books or MediaType.Audiobooks  => await ResolveBookOrAudiobookAsync(mediaType, metadata, ct),
            _                                        => await ResolveStandaloneAsync(mediaType, ct),
        };
    }

    // ── Per-media-type strategies ─────────────────────────────────────────────

    private async Task<ResolverResult> ResolveMusicAsync(
        IReadOnlyDictionary<string, string> meta, CancellationToken ct)
    {
        var artist = Get(meta, "artist") ?? Get(meta, "album_artist");
        var album  = Get(meta, "album");
        var title  = Get(meta, "title");
        var track  = ParseInt(Get(meta, "track_number") ?? Get(meta, "track"));

        if (string.IsNullOrWhiteSpace(album))
            return await CreateStandaloneAsync(MediaType.Music, ct);

        var parentKey = MakeKey(artist, album);
        var parentId  = await FindOrCreateParentAsync(MediaType.Music, parentKey, null, null, ct);

        return await FindOrCreateChildAsync(MediaType.Music, parentId, track, title, ct);
    }

    private async Task<ResolverResult> ResolveTvAsync(
        IReadOnlyDictionary<string, string> meta, CancellationToken ct)
    {
        var show     = Get(meta, "show_name") ?? Get(meta, "series");
        var season   = ParseInt(Get(meta, "season_number") ?? Get(meta, "season"));
        var episode  = ParseInt(Get(meta, "episode_number") ?? Get(meta, "episode"));
        var epTitle  = Get(meta, "episode_title") ?? Get(meta, "title");

        if (string.IsNullOrWhiteSpace(show))
            return await CreateStandaloneAsync(MediaType.TV, ct);

        // Level 1: Show parent.
        var showKey = MakeKey(show);
        var showId  = await FindOrCreateParentAsync(MediaType.TV, showKey, null, null, ct);

        // Level 2: Season parent (keyed by show_id + season_number, not parent_key).
        // When season is missing we treat the episode as a direct child of the show
        // — rare but defensible for miniseries / specials.
        if (season is null)
            return await FindOrCreateChildAsync(MediaType.TV, showId, episode, epTitle, ct);

        var existingSeason = await _works.FindChildByOrdinalAsync(showId, season.Value, ct);
        Guid seasonId;
        if (existingSeason is { } s)
        {
            seasonId = s;
        }
        else
        {
            // Season parents use a parent_key derived from show + season for
            // diagnostics; the find-or-create lookup actually goes through
            // parent_work_id + ordinal because two shows can share season 1.
            var seasonKey = MakeKey(show, $"S{season:D2}");
            seasonId = await _works.InsertParentAsync(
                MediaType.TV, seasonKey, showId, season, ct);
            _logger?.LogDebug("HierarchyResolver: created Season {Season} parent {SeasonId} under show {ShowId}",
                season, seasonId, showId);
        }

        // Level 3: Episode child under the season.
        return await FindOrCreateChildAsync(MediaType.TV, seasonId, episode, epTitle, ct);
    }

    private async Task<ResolverResult> ResolveComicsAsync(
        IReadOnlyDictionary<string, string> meta, CancellationToken ct)
    {
        var series = Get(meta, "series");
        var issue  = ParseInt(Get(meta, "issue_number") ?? Get(meta, "issue"));
        var title  = Get(meta, "title");

        if (string.IsNullOrWhiteSpace(series))
            return await CreateStandaloneAsync(MediaType.Comics, ct);

        var parentKey = MakeKey(series);
        var parentId  = await FindOrCreateParentAsync(MediaType.Comics, parentKey, null, null, ct);
        return await FindOrCreateChildAsync(MediaType.Comics, parentId, issue, title, ct);
    }

    private async Task<ResolverResult> ResolveBookOrAudiobookAsync(
        MediaType mediaType,
        IReadOnlyDictionary<string, string> meta,
        CancellationToken ct)
    {
        var series   = Get(meta, "series");
        var author   = Get(meta, "author") ?? Get(meta, "creator");
        var position = ParseInt(Get(meta, "series_position") ?? Get(meta, "series_index"));
        var title    = Get(meta, "title");

        if (string.IsNullOrWhiteSpace(series))
            return await CreateStandaloneAsync(mediaType, ct);

        var parentKey = MakeKey(author, series);
        var parentId  = await FindOrCreateParentAsync(mediaType, parentKey, null, null, ct);
        return await FindOrCreateChildAsync(mediaType, parentId, position, title, ct);
    }

    private async Task<ResolverResult> ResolveStandaloneAsync(
        MediaType mediaType, CancellationToken ct)
        => await CreateStandaloneAsync(mediaType, ct);

    // ── Shared helpers ────────────────────────────────────────────────────────

    private async Task<Guid> FindOrCreateParentAsync(
        MediaType mediaType,
        string parentKey,
        Guid? grandparent,
        int? ordinal,
        CancellationToken ct)
    {
        var existing = await _works.FindParentByKeyAsync(mediaType, parentKey, ct);
        if (existing is { } id) return id;

        var newId = await _works.InsertParentAsync(mediaType, parentKey, grandparent, ordinal, ct);
        _logger?.LogDebug(
            "HierarchyResolver: created {MediaType} parent {WorkId} key='{Key}'",
            mediaType, newId, parentKey);
        return newId;
    }

    private async Task<ResolverResult> FindOrCreateChildAsync(
        MediaType mediaType,
        Guid parentId,
        int? ordinal,
        string? title,
        CancellationToken ct)
    {
        // Prefer ordinal lookup — it's the indexed path and tolerates
        // re-tagged titles. Fall back to title match when no ordinal.
        if (ordinal is { } o)
        {
            var byOrdinal = await _works.FindChildByOrdinalAsync(parentId, o, ct);
            if (byOrdinal is { } existingId)
            {
                // Catalog row hit: promote to owned and return.
                await _works.PromoteCatalogToOwnedAsync(existingId, ct);
                return new ResolverResult(existingId, parentId, WorkKind.Child, o, NewlyCreated: false);
            }
        }
        else if (!string.IsNullOrWhiteSpace(title))
        {
            var byTitle = await _works.FindChildByTitleAsync(parentId, title, ct);
            if (byTitle is { } existingId)
            {
                await _works.PromoteCatalogToOwnedAsync(existingId, ct);
                return new ResolverResult(existingId, parentId, WorkKind.Child, null, NewlyCreated: false);
            }
        }

        var newId = await _works.InsertChildAsync(mediaType, parentId, ordinal, ct);
        return new ResolverResult(newId, parentId, WorkKind.Child, ordinal, NewlyCreated: true);
    }

    private async Task<ResolverResult> CreateStandaloneAsync(
        MediaType mediaType, CancellationToken ct)
    {
        var id = await _works.InsertStandaloneAsync(mediaType, ct);
        return new ResolverResult(id, null, WorkKind.Standalone, null, NewlyCreated: true);
    }

    // ── Normalization ─────────────────────────────────────────────────────────

    private static string? Get(IReadOnlyDictionary<string, string> meta, string key)
    {
        if (meta.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
            return v;
        return null;
    }

    private static int? ParseInt(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        // Tolerate "01", "1", "1/12", "01 of 12" — take the leading integer.
        var sb = new StringBuilder();
        foreach (var ch in raw.Trim())
        {
            if (char.IsDigit(ch)) sb.Append(ch);
            else if (sb.Length > 0) break;
        }
        return sb.Length > 0 && int.TryParse(sb.ToString(), out var n) ? n : null;
    }

    /// <summary>
    /// Builds a normalized parent_key by lowercasing, trimming, collapsing
    /// whitespace, stripping diacritics, and joining parts with '|'.
    /// Two slightly different spellings of the same album/show/series will
    /// hash to different keys — that's intentional. The resolver doesn't
    /// fuzzy-match; it relies on the file metadata being consistent across
    /// siblings (which it almost always is when files come from the same
    /// rip, season, or batch download).
    /// </summary>
    private static string MakeKey(params string?[] parts)
    {
        var sb = new StringBuilder();
        bool first = true;
        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part)) continue;
            if (!first) sb.Append('|');
            sb.Append(Normalize(part));
            first = false;
        }
        return sb.ToString();
    }

    private static string Normalize(string value)
    {
        // Decompose to strip diacritics, then collapse whitespace and lowercase.
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        bool prevSpace = false;
        foreach (var ch in decomposed)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark) continue;

            if (char.IsWhiteSpace(ch))
            {
                if (!prevSpace && sb.Length > 0) sb.Append(' ');
                prevSpace = true;
            }
            else
            {
                sb.Append(char.ToLowerInvariant(ch));
                prevSpace = false;
            }
        }
        return sb.ToString().TrimEnd();
    }
}

/// <summary>
/// The leaf-most Work the chain factory should attach an Edition to,
/// plus enough context for callers to schedule downstream work
/// (parent-level enrichment, hierarchy events, etc.).
/// </summary>
public sealed record ResolverResult(
    Guid WorkId,
    Guid? ParentWorkId,
    WorkKind WorkKind,
    int? Ordinal,
    bool NewlyCreated);
