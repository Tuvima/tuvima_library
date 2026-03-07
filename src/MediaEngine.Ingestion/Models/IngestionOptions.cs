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
    /// Tokenized path template applied by <see cref="Contracts.IFileOrganizer"/>.
    /// Supports conditional groups: <c>({Token})</c> — when the token value is empty,
    /// the entire group (parentheses + leading space) is collapsed.
    /// Example: <c>"{Category}/{HubName} ({Year})/{Format}/{HubName} ({Edition}){Ext}"</c>.
    /// </summary>
    public string OrganizationTemplate { get; set; } =
        "{Category}/{Author}/{Title}{Ext}";

    /// <summary>
    /// Holding area for files that cannot be auto-organized (low confidence,
    /// unknown media type, or "Other" category).  Keeps the Watch Folder clean.
    /// When empty, unresolved files remain in the Watch Folder.
    /// </summary>
    public string StagingDirectory { get; set; } = string.Empty;

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
}
