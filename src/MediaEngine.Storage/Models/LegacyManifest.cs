using System.Text.Json.Serialization;

namespace MediaEngine.Storage.Models;

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
    /// <summary>Comic and graphic novel oriented provider (e.g. Comic Vine).</summary>
    Comic,
    /// <summary>Film and TV oriented provider (e.g. TMDB, IMDb).</summary>
    Video,
    /// <summary>Podcast oriented provider (e.g. Apple Podcasts, Podcast Index).</summary>
    Podcasts,
    /// <summary>Music oriented provider (e.g. MusicBrainz, Spotify).</summary>
    Music,
}

/// <summary>
/// Root model for the legacy manifest.
/// Contains environment-level bootstrap settings for the platform.
/// Spec: Phase 4 – Configuration Management responsibility.
/// </summary>
public sealed class LegacyManifest
{
    /// <summary>Manifest format version. Increment when the shape changes.</summary>
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; set; } = "1.0";

    /// <summary>
    /// Path to the SQLite database file.
    /// Relative paths are resolved from the manifest's own directory.
    /// Spec: "database file and the legacy manifest MUST reside in the same
    /// root directory or a designated application data folder."
    /// </summary>
    [JsonPropertyName("database_path")]
    public string DatabasePath { get; set; } = "library.db";

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
    /// Kept for backward compatibility during legacy manifest migration.
    /// Current provider endpoints are configured per-provider in <c>config/providers/</c>.
    /// </summary>
    [JsonPropertyName("provider_endpoints")]
    public Dictionary<string, string> ProviderEndpoints { get; set; } = [];

    /// <summary>
    /// Per-property overrides for the Wikidata property map.
    /// Each entry targets a P-code and may override the claim key, confidence,
    /// or enabled state of a default property — or define an entirely new one.
    /// Kept for backward compatibility during legacy manifest migration.
    /// </summary>
    [JsonPropertyName("wikidata_property_map")]
    public List<WikidataPropertyMapOverride> WikidataPropertyMap { get; set; } = [];

    /// <summary>Settings for the affiliate link generator.</summary>
    [JsonPropertyName("affiliate")]
    public AffiliateSettings Affiliate { get; set; } = new();
}

/// <summary>
/// A user-defined override for a single Wikidata property from the legacy manifest.
/// Only non-null fields replace the current default value for that property.
/// </summary>
public sealed class WikidataPropertyMapOverride
{
    /// <summary>The Wikidata property code to override, e.g. <c>"P179"</c>.</summary>
    [JsonPropertyName("p_code")]
    public string PCode { get; set; } = string.Empty;

    /// <summary>Override the claim key. <c>null</c> = keep default.</summary>
    [JsonPropertyName("claim_key")]
    public string? ClaimKey { get; set; }

    /// <summary>Override the confidence. <c>null</c> = keep default.</summary>
    [JsonPropertyName("confidence")]
    public double? Confidence { get; set; }

    /// <summary>Disable this property entirely. <c>null</c> = keep default (<c>true</c>).</summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }
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
    /// These values are loaded from provider config files and injected into
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

    /// <summary>
    /// Interval in days between automatic weekly metadata sync runs.
    /// The <c>WeeklyMetadataSyncService</c> re-harvests all entities with
    /// a <c>wikidata_qid</c> canonical value on this schedule.
    /// Default: 7 days. Set to 0 to disable automatic syncing.
    /// </summary>
    [JsonPropertyName("weekly_sync_interval_days")]
    public int WeeklySyncIntervalDays { get; set; } = 7;

    /// <summary>
    /// Number of entities to enqueue per batch during weekly sync.
    /// Smaller batches reduce channel back-pressure; larger batches finish sooner.
    /// Default: 50.
    /// </summary>
    [JsonPropertyName("weekly_sync_batch_size")]
    public int WeeklySyncBatchSize { get; set; } = 50;

    /// <summary>
    /// Milliseconds to wait between enqueuing batches during weekly sync.
    /// Prevents the harvest channel (DropOldest, 500 capacity) from dropping
    /// fresh ingestion requests during a large sync run.
    /// Default: 2000 ms.
    /// </summary>
    [JsonPropertyName("weekly_sync_batch_delay_ms")]
    public int WeeklySyncBatchDelayMs { get; set; } = 2000;

    /// <summary>
    /// Interval in hours between automatic library reconciliation scans.
    /// The <c>LibraryReconciliationService</c> checks that every Normal-status
    /// asset's file still exists on disk.
    /// Default: 24 hours. Set to 0 to disable automatic reconciliation.
    /// </summary>
    [JsonPropertyName("reconciliation_interval_hours")]
    public int ReconciliationIntervalHours { get; set; } = 24;

    /// <summary>
    /// Number of days to retain rejected files in <c>.staging/rejected/</c>
    /// before the <c>RejectedFileCleanupService</c> permanently deletes them.
    /// Set to 0 to disable automatic cleanup of rejected files.
    /// Default: 30 days.
    /// </summary>
    [JsonPropertyName("rejected_retention_days")]
    public int RejectedRetentionDays { get; set; } = 30;

    /// <summary>
    /// Interval in days between periodic edition re-checks. For items matched at
    /// "work" level in edition-aware media types, the weekly sync retries bridge
    /// resolution to see if a Wikidata edition entity has been created.
    /// </summary>
    [JsonPropertyName("edition_recheck_interval_days")]
    public int EditionRecheckIntervalDays { get; set; } = 7;

    /// <summary>
    /// Cron expressions for all background services. Keys are service names,
    /// values are standard 5-field cron expressions. Centralised here so all
    /// schedules are visible and tuneable in one place.
    ///
    /// <para>Recognised keys (all default to overnight, low-traffic windows):</para>
    /// <list type="bullet">
    ///   <item><c>activity_pruning</c> — ActivityPruningService (default: 3 AM daily)</item>
    ///   <item><c>library_reconciliation</c> — LibraryReconciliationService (default: 5 AM daily)</item>
    ///   <item><c>missing_universe_sweep</c> — MissingUniverseSweepService (default: 4 AM Sundays)</item>
    ///   <item><c>rejected_file_cleanup</c> — RejectedFileCleanupService (default: 4 AM daily)</item>
    ///   <item><c>universe_enrichment</c> — UniverseEnrichmentService (default: 3 AM daily)</item>
    ///   <item><c>pass2_nightly_sweep</c> — Pass 2 hydration sweep (default: 2 AM daily)</item>
    ///   <item><c>vibe_batch</c> — AI vibe tagging batch (default: 4 AM daily)</item>
    ///   <item><c>series_check</c> — AI series alignment check (default: 3 AM daily)</item>
    ///   <item><c>whisper_bake</c> — Whisper audio bake (default: 2 AM daily)</item>
    ///   <item><c>taste_profile_update</c> — Taste profile update (default: 5 AM Sundays)</item>
    ///   <item><c>description_intelligence</c> — Description intelligence batch (default: every 15 min)</item>
    /// </list>
    ///
    /// Missing keys fall back to each service's hardcoded default, so existing
    /// deployments without this section continue to work unchanged.
    /// </summary>
    [JsonPropertyName("schedules")]
    public Dictionary<string, string> Schedules { get; set; } = new()
    {
        ["activity_pruning"]         = "0 3 * * *",
        ["library_reconciliation"]   = "0 5 * * *",
        ["missing_universe_sweep"]   = "0 4 * * 0",
        ["rejected_file_cleanup"]    = "0 4 * * *",
        ["universe_enrichment"]      = "0 3 * * *",
        ["pass2_nightly_sweep"]      = "0 2 * * *",
        ["vibe_batch"]               = "0 4 * * *",
        ["series_check"]             = "0 3 * * *",
        ["whisper_bake"]             = "0 2 * * *",
        ["taste_profile_update"]     = "0 5 * * 0",
        ["description_intelligence"] = "*/15 * * * *",
    };
}

/// <summary>
/// Settings for the affiliate link generator.
/// Links are built from bridge IDs (ASIN, Apple Books ID, Goodreads ID, TMDB ID)
/// stored as canonical values after Wikidata hydration.
/// </summary>
public sealed class AffiliateSettings
{
    /// <summary>
    /// Amazon Associates tag appended to all Amazon product links.
    /// When set, links take the form <c>https://www.amazon.com/dp/{ASIN}?tag={tag}</c>.
    /// <c>null</c> or empty = no affiliate tag appended.
    /// </summary>
    [JsonPropertyName("amazon_affiliate_tag")]
    public string? AmazonAffiliateTag { get; set; }

    /// <summary>
    /// When <c>true</c>, the Dashboard displays the required transparency disclosure
    /// near affiliate links: "As an Amazon Associate, I earn from qualifying purchases."
    /// Default: <c>true</c>.
    /// </summary>
    [JsonPropertyName("show_affiliate_disclosure")]
    public bool ShowAffiliateDisclosure { get; set; } = true;
}
