using System.IO;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Ingestion.Models;

/// <summary>
/// Represents a single entry from <c>config/libraries.json</c>.
/// Populated during startup PostConfigure and made available to the
/// ingestion pipeline so media type disambiguation can use the
/// library folder's configured media types as a strong prior.
/// </summary>
public sealed class LibraryFolderEntry
{
    /// <summary>
    /// Legacy single source path monitored by this library folder.
    /// New code should prefer <see cref="SourcePaths"/> (which includes this path
    /// as its first element when set). Kept as an <c>init</c> property so existing
    /// call sites that construct entries with a single path continue to compile.
    /// </summary>
    public string SourcePath { get; init; } = string.Empty;

    /// <summary>
    /// All source paths belonging to this logical library. A single library can
    /// span multiple drives (e.g. <c>D:\Movies</c> and <c>E:\Movies</c> as one
    /// Movies library), the same way Plex and Jellyfin already allow. Files always
    /// reorganise in place within whichever source path they already live in —
    /// Tuvima never moves files across source paths during organise.
    /// Spec: side-by-side-with-Plex plan §F.
    /// </summary>
    public IReadOnlyList<string> SourcePaths { get; init; } = [];

    /// <summary>
    /// The effective list of source paths. Prefers <see cref="SourcePaths"/>
    /// when populated; otherwise returns a singleton list derived from the
    /// legacy <see cref="SourcePath"/> field. Always use this when walking
    /// a library's paths.
    /// </summary>
    public IReadOnlyList<string> EffectiveSourcePaths =>
        SourcePaths.Count > 0
            ? SourcePaths
            : (string.IsNullOrWhiteSpace(SourcePath) ? [] : new[] { SourcePath });

    /// <summary>
    /// Media types configured for this library folder (e.g. Epub, Audiobook).
    /// Parsed from the JSON <c>media_types</c> string array at startup.
    /// </summary>
    public IReadOnlyList<MediaType> MediaTypes { get; init; } = [];

    /// <summary>
    /// Hard read-only gate. When <see langword="true"/>, the ingestion pipeline
    /// will never move, rename, or tag files that belong to this library — they
    /// are indexed in place. The escape hatch for users who want Tuvima to
    /// mirror an external library (e.g. a Plex tree) without ever touching it.
    /// Spec: side-by-side-with-Plex plan §I.
    /// </summary>
    public bool ReadOnly { get; init; }

    /// <summary>
    /// Per-library override for metadata writeback. <see langword="null"/> means
    /// use the global writeback flag; <see langword="true"/> or <see langword="false"/>
    /// forces on/off for this library only. Spec: side-by-side-with-Plex plan §I.
    /// </summary>
    public bool? WritebackOverride { get; init; }
}

/// <summary>
/// Runtime options that control the ingestion pipeline behaviour.
/// Bind from <c>appsettings.json</c> (section <c>"Ingestion"</c>) via
/// <c>services.Configure&lt;IngestionOptions&gt;(config.GetSection("Ingestion"))</c>.
///
/// Spec: Phase 7 – Configuration § Ingestion Settings.
/// </summary>
public sealed class IngestionOptions
{
    public const string SectionName = "Ingestion";

    /// <summary>
    /// Absolute path of the "Watch" folder monitored by the file watcher.
    /// Required.  The process must have read access to this directory.
    /// </summary>
    public string WatchDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Root of the organised library into which accepted files are moved.
    /// Required when <see cref="AutoOrganize"/> is <see langword="true"/>.
    /// </summary>
    public string LibraryRoot { get; set; } = string.Empty;

    /// <summary>
    /// Default tokenized path template applied by <see cref="Contracts.IFileOrganizer"/>
    /// when no media-type-specific template matches in <see cref="OrganizationTemplates"/>.
    /// Supports conditional groups: <c>({Token})</c> — when the token value is empty,
    /// the entire group (parentheses + leading space) is collapsed.
    /// </summary>
    public string OrganizationTemplate { get; set; } =
        "{Category}/{Title} ({Year})/{Title}{Ext}";

    /// <summary>
    /// Per-media-type organisation templates.  Keys are media type names
    /// (e.g. "Books", "Audiobooks", "Movies", "TV", "Comic", "Music")
    /// or "default".  Values are tokenised path templates.
    /// Fallback chain: media-type-specific → "default" → <see cref="OrganizationTemplate"/>
    /// → hardcoded <c>{Category}/{Title}/{Title}{Ext}</c>.
    /// </summary>
    // ──────────────────────────────────────────────────────────────────
    // Default templates: Plex / Jellyfin / Audiobookshelf compatible.
    // Side-by-side-with-Plex plan §A. Bridge-ID groups (`{{imdb-{ImdbId}}}`)
    // collapse cleanly when the ID is missing because the inner token resolves
    // to empty and the surrounding literal `{...}` becomes `{}` which the
    // outer cleanup pass strips. The legacy `{Qid}` token stays available for
    // power users but is no longer in the defaults.
    // ──────────────────────────────────────────────────────────────────
    public Dictionary<string, string> OrganizationTemplates { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["default"]    = "{Category}/{Title} ({Year})/{Title}{Ext}",
        // Movies: Plex convention — `Title (Year)/Title (Year)`
        ["Movies"]     = "Movies/{Title} ({Year})/{Title} ({Year}){Ext}",
        // TV: Plex convention — `Show (Year)/Season XX/Show - sXXeYY - Title`
        ["TV"]         = "TV/{Series} ({Year})/Season {Season}/{Series} - s{Season}e{Episode} - {EpisodeTitle}{Ext}",
        // Music: Picard / Plex convention — `Artist/Album (Year)/[Disc]## - Title`
        // {Disc?} optional segment expands to e.g. "Disc 02/" for multi-disc
        // releases and collapses entirely for single-disc albums.
        ["Music"]      = "Music/{Artist}/{Album} ({Year})/{TrackNumber} - {Title}{Ext}",
        // Audiobooks: Author/Title (Year)/Title — matches Music pattern
        ["Audiobooks"] = "Audiobooks/{Author}/{Title} ({Year})/{Title}{Ext}",
        // Books: Author/Title (Year)/Title (Year) — matches Music pattern
        ["Books"]      = "Books/{Author}/{Title} ({Year})/{Title} ({Year}){Ext}",

        // Comics: Komga / Mylar / Kavita convention — `Series/Series - NNN (Year)`
        ["Comic"]      = "Comics/{Series}/{Series} - {IssueNumber} ({Year}){Ext}",
    };

    /// <summary>
    /// Path to the staging directory: {LibraryRoot}/.data/staging/.
    /// All ingested files land here first, awaiting hydration and promotion
    /// to the organised library. Files that cannot be identified remain here
    /// for manual review.
    /// Derived from LibraryRoot — not independently configurable.
    /// </summary>
    public string StagingPath => string.IsNullOrWhiteSpace(LibraryRoot)
        ? string.Empty
        : Path.Combine(LibraryRoot, ".data", "staging");

    /// <summary>
    /// Backward-compatible alias for <see cref="StagingPath"/>.
    /// </summary>
    [Obsolete("Use StagingPath instead. This property will be removed in a future release.")]
    public string OrphanagePath => StagingPath;

    /// <summary>
    /// When <see langword="true"/> the engine automatically moves accepted files
    /// to <see cref="LibraryRoot"/> using <see cref="OrganizationTemplate"/>.
    /// Default: <see langword="false"/> (safe mode — monitor only).
    /// </summary>
    public bool AutoOrganize { get; set; }

    /// <summary>
    /// When <see langword="true"/> the engine calls <see cref="Contracts.IMetadataTagger"/>
    /// to embed resolved metadata back into supported file formats.
    /// Default: <see langword="false"/>.
    /// </summary>
    public bool WriteBack { get; set; }

    /// <summary>
    /// Whether to also watch sub-directories of <see cref="WatchDirectory"/>.
    /// Default: <see langword="true"/>.
    /// </summary>
    public bool IncludeSubdirectories { get; set; } = true;

    // ── Polling Fallback ────────────────────────────────────────────

    /// <summary>
    /// Interval in seconds between polling sweeps of the Watch Folder.
    /// Acts as a safety net when <see cref="System.IO.FileSystemWatcher"/>
    /// misses OS events. Set to 0 to disable polling. Default: 30.
    /// </summary>
    public int PollIntervalSeconds { get; set; } = 30;

    // ── Language configuration ────────────────────────────────────────

    /// <summary>
    /// The configured library language (ISO 639-1 code, e.g. "en", "fr", "de").
    /// Populated from <c>CoreConfiguration.Language</c> by the PostConfigure hook.
    /// Files whose embedded language tag does not match this value are routed
    /// to the review queue with trigger <c>ReviewTrigger.LanguageMismatch</c>.
    /// Default: "en".
    /// </summary>
    public string ConfiguredLanguage { get; set; } = "en";

    // ── Media Type Disambiguation ────────────────────────────────────

    /// <summary>
    /// Minimum confidence for auto-assigning a media type without review.
    /// Populated from <c>config/disambiguation.json</c> at startup.
    /// Default: 0.70.
    /// </summary>
    public double MediaTypeAutoAssignThreshold { get; set; } = 0.70;

    /// <summary>
    /// Minimum confidence for creating a review queue entry (provisional assignment).
    /// Below this threshold, the file is assigned <c>MediaType.Unknown</c>.
    /// Populated from <c>config/disambiguation.json</c> at startup.
    /// Default: 0.40.
    /// </summary>
    public double MediaTypeReviewThreshold { get; set; } = 0.40;

    // ── Library Folder Priors ─────────────────────────────────────────

    /// <summary>
    /// Library folder entries loaded from <c>config/libraries.json</c>.
    /// Each entry maps a source path to its configured media types so that
    /// the ingestion pipeline can apply a strong media type prior when a
    /// file arrives from a folder whose content category is known.
    /// Populated by the PostConfigure hook in Program.cs at startup.
    /// </summary>
    public IReadOnlyList<LibraryFolderEntry> LibraryFolders { get; set; } = [];

    // ── Template Resolution ────────────────────────────────────────────

    private const string HardcodedFallback = "{Category}/{Title}/{Title}{Ext}";

    /// <summary>
    /// Resolves the organisation template for a given media type.
    /// Fallback chain: media-type-specific → "default" key → <see cref="OrganizationTemplate"/>
    /// → hardcoded <c>{Category}/{Title}/{Title}{Ext}</c>.
    /// </summary>
    public string ResolveTemplate(string? mediaTypeName)
    {
        // 1. Try media-type-specific template.
        if (!string.IsNullOrWhiteSpace(mediaTypeName)
            && OrganizationTemplates.TryGetValue(mediaTypeName, out var specific)
            && !string.IsNullOrWhiteSpace(specific))
        {
            return specific;
        }

        // 2. Try "default" key in templates dictionary.
        if (OrganizationTemplates.TryGetValue("default", out var def)
            && !string.IsNullOrWhiteSpace(def))
        {
            return def;
        }

        // 3. Fall back to OrganizationTemplate property.
        if (!string.IsNullOrWhiteSpace(OrganizationTemplate))
            return OrganizationTemplate;

        // 4. Hardcoded fallback.
        return HardcodedFallback;
    }
}
