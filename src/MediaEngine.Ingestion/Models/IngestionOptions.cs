using System.IO;

namespace MediaEngine.Ingestion.Models;

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
        "{Category}/{Title} - {Qid}/{Title}{Ext}";

    /// <summary>
    /// Per-media-type organisation templates.  Keys are media type names
    /// (e.g. "Books", "Audiobooks", "Movies", "TV", "Comic", "Music", "Podcasts")
    /// or "default".  Values are tokenised path templates.
    /// Fallback chain: media-type-specific → "default" → <see cref="OrganizationTemplate"/>
    /// → hardcoded <c>{Category}/{Title}/{Title}{Ext}</c>.
    /// </summary>
    public Dictionary<string, string> OrganizationTemplates { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        // Books & Audiobooks: format subfolder distinguishes reading vs listening formats
        ["Books"]      = "{Category}/{Title} - {Qid}/{Format}/{Title}{Ext}",
        ["Audiobooks"] = "{Category}/{Title} - {Qid}/{Format}/{Title}{Ext}",
        // TV: season/episode in the filename
        ["TV"]         = "{Category}/{Title} - {Qid}/S{Season}E{Episode} - {Title}{Ext}",
        // Music: artist → album directory structure
        ["Music"]      = "{Category}/{Artist}/{Album} - {Qid}/{TrackNumber} - {Title}{Ext}",
    };

    /// <summary>
    /// Path to the orphanage directory: {LibraryRoot}/.orphans/.
    /// Files that cannot be auto-organized are quarantined here.
    /// Derived from LibraryRoot — not independently configurable.
    /// </summary>
    public string OrphanagePath => string.IsNullOrWhiteSpace(LibraryRoot)
        ? string.Empty
        : Path.Combine(LibraryRoot, ".orphans");

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
