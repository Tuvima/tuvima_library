using System.Text.RegularExpressions;
using MediaEngine.Domain;

namespace MediaEngine.Ingestion.Detection;

/// <summary>
/// Parses curated-library filename and folder hints out of a media file's
/// path. Recognises the bracket formats Plex and Jellyfin write into folder
/// names for movies and TV shows, plus Tuvima's legacy <c>(Q12345)</c> form.
///
/// When a library has been curated by another media manager the bridge IDs
/// (IMDB, TMDB, TVDB) are sitting right in the path — parsing them here and
/// pre-seeding the bridge cache lets Stage 1 resolve the work with zero
/// external API calls. The parser is a pure function over the path string:
/// it never touches disk, never hits the network.
///
/// Spec: side-by-side-with-Plex plan §G.
/// </summary>
public static class OrganizationHintParser
{
    // ── Recognisers ─────────────────────────────────────────────────────
    //
    // Patterns are tuned to the shapes users actually write. We accept both
    // the Plex `{id-value}` form and the Jellyfin `[idid-value]` form across
    // the whole path (folder *or* filename). The regexes are case-insensitive
    // so variations like `{IMDB-...}` still match.

    private static readonly Regex PlexImdb = new(
        @"\{imdb-(tt\d+)\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PlexTmdb = new(
        @"\{tmdb-(\d+)\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PlexTvdb = new(
        @"\{tvdb-(\d+)\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PlexEdition = new(
        @"\{edition-([^}]+)\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex JellyfinImdb = new(
        @"\[imdbid-(tt\d+)\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex JellyfinTmdb = new(
        @"\[tmdbid-(\d+)\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex JellyfinTvdb = new(
        @"\[tvdbid-(\d+)\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Tuvima legacy QID marker — kept so existing Tuvima-organised libraries
    // continue to resolve without re-running identity.
    private static readonly Regex TuvimaQid = new(
        @"\(Q(\d+)\)", RegexOptions.Compiled);

    // Extras subfolder names — recognised so items inside them are tagged
    // as extras rather than main works. Match by whole folder component.
    private static readonly HashSet<string> ExtrasFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "behind the scenes",
        "deleted scenes",
        "featurettes",
        "interviews",
        "scenes",
        "shorts",
        "trailers",
        "other",
        "extras",
    };

    /// <summary>
    /// Parses a media file's path and returns any bridge IDs, edition
    /// labels, or extras-folder classification that can be derived from it.
    /// Returns an empty <see cref="OrganizationHints"/> when nothing is
    /// found — never returns <see langword="null"/>.
    /// </summary>
    public static OrganizationHints Parse(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return OrganizationHints.Empty;

        var bridgeIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // IMDB — Plex first, then Jellyfin. Normalise `tt` prefix to lowercase.
        var imdbMatch = PlexImdb.Match(path);
        if (!imdbMatch.Success) imdbMatch = JellyfinImdb.Match(path);
        if (imdbMatch.Success)
            bridgeIds[BridgeIdKeys.ImdbId] = imdbMatch.Groups[1].Value.ToLowerInvariant();

        // TMDB
        var tmdbMatch = PlexTmdb.Match(path);
        if (!tmdbMatch.Success) tmdbMatch = JellyfinTmdb.Match(path);
        if (tmdbMatch.Success)
            bridgeIds[BridgeIdKeys.TmdbId] = tmdbMatch.Groups[1].Value;

        // TVDB
        var tvdbMatch = PlexTvdb.Match(path);
        if (!tvdbMatch.Success) tvdbMatch = JellyfinTvdb.Match(path);
        if (tvdbMatch.Success)
            bridgeIds[BridgeIdKeys.TvdbId] = tvdbMatch.Groups[1].Value;

        // Tuvima legacy QID
        var qidMatch = TuvimaQid.Match(path);
        if (qidMatch.Success)
            bridgeIds[BridgeIdKeys.WikidataQid] = "Q" + qidMatch.Groups[1].Value;

        // Edition label — always Plex form; Jellyfin uses a suffix after a
        // separator which is ambiguous without more context and is left to
        // the higher-level edition splitter.
        string? editionLabel = null;
        var editionMatch = PlexEdition.Match(path);
        if (editionMatch.Success)
            editionLabel = editionMatch.Groups[1].Value.Trim();

        // Extras detection — walk folder components.
        bool isExtras = false;
        var dir = Path.GetDirectoryName(path);
        while (!string.IsNullOrEmpty(dir))
        {
            var name = Path.GetFileName(dir);
            if (!string.IsNullOrEmpty(name) && ExtrasFolderNames.Contains(name))
            {
                isExtras = true;
                break;
            }

            var parent = Path.GetDirectoryName(dir);
            if (parent == dir) break;
            dir = parent;
        }

        if (bridgeIds.Count == 0 && editionLabel is null && !isExtras)
            return OrganizationHints.Empty;

        return new OrganizationHints(bridgeIds, editionLabel, isExtras);
    }
}

/// <summary>
/// Structured hints extracted from a media file's path by
/// <see cref="OrganizationHintParser"/>. All fields are immutable.
/// </summary>
public sealed class OrganizationHints
{
    public static readonly OrganizationHints Empty = new(
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        editionLabel: null,
        isExtras: false);

    /// <summary>
    /// Bridge IDs keyed by <see cref="BridgeIdKeys"/> constants.
    /// Values include <c>imdb_id</c>, <c>tmdb_id</c>, <c>tvdb_id</c>,
    /// and <c>wikidata_qid</c> when any are present in the path.
    /// </summary>
    public IReadOnlyDictionary<string, string> BridgeIds { get; }

    /// <summary>
    /// Edition label extracted from a Plex <c>{edition-…}</c> bracket,
    /// e.g. <c>"Director's Cut"</c>. <see langword="null"/> when absent.
    /// </summary>
    public string? EditionLabel { get; }

    /// <summary>
    /// <see langword="true"/> when the file lives under a recognised
    /// extras subfolder (<c>Behind The Scenes/</c>, <c>Trailers/</c>, etc.)
    /// and should be stored as an extra rather than a main work.
    /// </summary>
    public bool IsExtras { get; }

    /// <summary>
    /// <see langword="true"/> when the parser found anything at all.
    /// </summary>
    public bool HasHints => BridgeIds.Count > 0 || EditionLabel is not null || IsExtras;

    public OrganizationHints(
        IReadOnlyDictionary<string, string> bridgeIds,
        string? editionLabel,
        bool isExtras)
    {
        BridgeIds    = bridgeIds;
        EditionLabel = editionLabel;
        IsExtras     = isExtras;
    }
}
