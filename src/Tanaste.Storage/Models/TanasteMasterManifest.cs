using System.Text.Json.Serialization;

namespace Tanaste.Storage.Models;

/// <summary>
/// The media domain a provider specialises in.
/// Used for UI grouping and as metadata when building scoring contexts;
/// the Intelligence engine itself is domain-agnostic.
/// Spec: Phase 8 – Categorized Provider Registry.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProviderDomain
{
    /// <summary>Applies across all media types (e.g. Wikidata, local filesystem).</summary>
    Universal,
    /// <summary>E-book oriented provider (e.g. Open Library, Apple Books for books).</summary>
    Ebook,
    /// <summary>Audiobook oriented provider (e.g. Audnexus, Apple Books for audio).</summary>
    Audiobook,
    /// <summary>Film and TV oriented provider (e.g. TMDB, IMDb).</summary>
    Video,
}

/// <summary>
/// Root model for <c>tanaste_master.json</c>.
/// Contains environment-level bootstrap settings for the Tanaste platform.
/// Spec: Phase 4 – Configuration Management responsibility.
/// </summary>
public sealed class TanasteMasterManifest
{
    /// <summary>Manifest format version. Increment when the shape changes.</summary>
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; set; } = "1.0";

    /// <summary>
    /// Path to the SQLite database file.
    /// Relative paths are resolved from the manifest's own directory.
    /// Spec: "database file and tanaste_master.json MUST reside in the same
    /// root directory or a designated application data folder."
    /// </summary>
    [JsonPropertyName("database_path")]
    public string DatabasePath { get; set; } = "tanaste.db";

    /// <summary>
    /// Root directory for media file storage.
    /// No BLOBs are stored in the database; all binaries live here.
    /// </summary>
    [JsonPropertyName("data_root")]
    public string DataRoot { get; set; } = "./media";

    /// <summary>
    /// Directory that the FileSystemWatcher monitors for new incoming files.
    /// When set, this value overrides the <c>Ingestion:WatchDirectory</c> entry
    /// in <c>appsettings.json</c> at startup via <c>PostConfigure&lt;IngestionOptions&gt;</c>.
    /// Empty string = use <c>appsettings.json</c> as fallback.
    /// </summary>
    [JsonPropertyName("watch_directory")]
    public string WatchDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Root directory where the organised media library lives.
    /// When set, this value overrides <c>Ingestion:LibraryRoot</c> in
    /// <c>appsettings.json</c> at startup.
    /// </summary>
    [JsonPropertyName("library_root")]
    public string LibraryRoot { get; set; } = string.Empty;

    /// <summary>
    /// Tokenized path template for file organization.
    /// When set (non-empty), overrides the <c>Ingestion:OrganizationTemplate</c>
    /// default from <c>appsettings.json</c>.
    /// Supports conditional groups: <c>({Token})</c> — when the token is empty,
    /// the parentheses and leading space are collapsed.
    /// </summary>
    [JsonPropertyName("organization_template")]
    public string OrganizationTemplate { get; set; } = string.Empty;

    /// <summary>Provider bootstrap entries loaded before the provider_registry table is queried.</summary>
    [JsonPropertyName("providers")]
    public List<ProviderBootstrap> Providers { get; set; } = [];

    /// <summary>Settings governing background maintenance tasks.</summary>
    [JsonPropertyName("maintenance")]
    public MaintenanceSettings Maintenance { get; set; } = new();

    /// <summary>
    /// Thresholds and tuning parameters for the Intelligence &amp; Scoring Engine.
    /// Spec: Phase 6 – Threshold Enforcement; Weight Management.
    /// </summary>
    [JsonPropertyName("scoring")]
    public ScoringSettings Scoring { get; set; } = new();

    /// <summary>
    /// Base URLs for external metadata provider APIs.
    /// Keys match adapter names used in named <c>HttpClient</c> registrations:
    /// <c>apple_books</c>, <c>audnexus</c>, <c>wikidata_api</c>,
    /// <c>wikidata_sparql</c>.
    ///
    /// Keeping URLs here (not hard-coded in adapters) means a provider endpoint
    /// change requires only a <c>tanaste_master.json</c> edit — no recompile.
    /// Spec: Phase 9 – External Metadata Adapters § URL Management.
    /// </summary>
    [JsonPropertyName("provider_endpoints")]
    public Dictionary<string, string> ProviderEndpoints { get; set; } = [];
}

/// <summary>
/// Lightweight descriptor for a provider that should be registered on first run.
/// Full configuration lives in <c>provider_config</c> (database).
/// </summary>
public sealed class ProviderBootstrap
{
    /// <summary>Must match <c>provider_registry.name</c>.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    /// <summary>Maps to <c>provider_registry.is_enabled</c>.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Default scoring weight for this provider across all metadata fields.
    /// Consumed by the scoring engine; not stored in this table.
    /// Individual fields can be overridden via <see cref="FieldWeights"/>.
    /// </summary>
    [JsonPropertyName("weight")]
    public double Weight { get; set; } = 1.0;

    /// <summary>
    /// The media domain this provider specialises in.
    /// Informational: used for UI grouping and future domain-filtered scoring.
    /// Spec: Phase 8 – Categorized Provider Registry § Domain.
    /// </summary>
    [JsonPropertyName("domain")]
    public ProviderDomain Domain { get; set; } = ProviderDomain.Universal;

    /// <summary>
    /// Declarative list of metadata fields this provider is considered an expert in
    /// (e.g. <c>["cover", "narrator", "series"]</c>).
    /// Informational: shown in the UI to explain why a particular provider's value
    /// was chosen; the actual trust level is encoded in <see cref="FieldWeights"/>.
    /// Spec: Phase 8 – Categorized Provider Registry § Capability Tags.
    /// </summary>
    [JsonPropertyName("capability_tags")]
    public List<string> CapabilityTags { get; set; } = [];

    /// <summary>
    /// Per-field weight overrides that replace <see cref="Weight"/> for specific
    /// metadata fields.  Key = claim key (e.g. <c>"cover"</c>, <c>"narrator"</c>,
    /// <c>"series"</c>).  Value = weight in [0.0, 1.0].
    ///
    /// When the scoring engine resolves a field, it looks up the provider's entry
    /// here first; if absent, it falls back to <see cref="Weight"/>.
    ///
    /// These values are loaded from <c>tanaste_master.json</c> and injected into
    /// <c>ScoringContext.ProviderFieldWeights</c> at scoring time — never hard-coded.
    /// Spec: Phase 8 – Field-Level Weight Matrix.
    /// </summary>
    [JsonPropertyName("field_weights")]
    public Dictionary<string, double> FieldWeights { get; set; } = [];
}

/// <summary>
/// Threshold and decay parameters for the Phase 6 Intelligence &amp; Scoring Engine.
/// All thresholds are in the [0.0, 1.0] probability range.
/// </summary>
public sealed class ScoringSettings
{
    /// <summary>
    /// Minimum confidence score required for the arbiter to automatically link
    /// a Work to an existing Hub without human review.
    /// Spec: Phase 6 – Hub Integrity invariant.
    /// </summary>
    [JsonPropertyName("auto_link_threshold")]
    public double AutoLinkThreshold { get; set; } = 0.85;

    /// <summary>
    /// Scores at or above this value but below <see cref="AutoLinkThreshold"/>
    /// are flagged as NeedsReview rather than auto-linked or rejected.
    /// Spec: Phase 6 – Low Confidence Flags.
    /// </summary>
    [JsonPropertyName("conflict_threshold")]
    public double ConflictThreshold { get; set; } = 0.60;

    /// <summary>
    /// When the runner-up value's normalised weight is within this margin of the
    /// winner's weight, the field is flagged as conflicted.
    /// Smaller values = stricter conflict detection.
    /// </summary>
    [JsonPropertyName("conflict_epsilon")]
    public double ConflictEpsilon { get; set; } = 0.05;

    /// <summary>
    /// Claims older than this many days receive a time-decay multiplier.
    /// Set to 0 to disable stale-claim decay entirely.
    /// Spec: Phase 6 – Stale Claim Handling.
    /// </summary>
    [JsonPropertyName("stale_claim_decay_days")]
    public int StaleClaimDecayDays { get; set; } = 90;

    /// <summary>
    /// Weight multiplier applied to claims older than <see cref="StaleClaimDecayDays"/>.
    /// Must be in (0.0, 1.0]; default 0.8 reduces stale-claim influence by 20 %.
    /// </summary>
    [JsonPropertyName("stale_claim_decay_factor")]
    public double StaleClaimDecayFactor { get; set; } = 0.8;
}

/// <summary>Parameters for background housekeeping tasks.</summary>
public sealed class MaintenanceSettings
{
    /// <summary>
    /// <c>transaction_log</c> rows beyond this threshold are pruned.
    /// Spec: "SHOULD be archived or truncated after reaching 100,000 entries."
    /// </summary>
    [JsonPropertyName("max_transaction_log_entries")]
    public int MaxTransactionLogEntries { get; set; } = 100_000;

    /// <summary>
    /// When <c>true</c>, a VACUUM is issued during the startup sequence.
    /// Spec: "SHOULD perform a VACUUM during low-activity maintenance windows."
    /// </summary>
    [JsonPropertyName("vacuum_on_startup")]
    public bool VacuumOnStartup { get; set; } = false;

    /// <summary>
    /// Number of days to retain system activity log entries.
    /// Entries older than this are pruned daily by <c>ActivityPruningService</c>.
    /// Default: 60 days.
    /// </summary>
    [JsonPropertyName("activity_retention_days")]
    public int ActivityRetentionDays { get; set; } = 60;
}
