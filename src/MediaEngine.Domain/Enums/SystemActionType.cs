namespace MediaEngine.Domain.Enums;

/// <summary>
/// String constants for the <c>action_type</c> column in <c>system_activity</c>.
///
/// Stored as TEXT in SQLite for readability and extensibility — future phases
/// can introduce new action types without a schema migration.
/// </summary>
public static class SystemActionType
{
    /// <summary>A file was ingested into the library.</summary>
    public const string FileIngested = "FileIngested";

    /// <summary>An entity was deeply enriched via Wikidata SPARQL.</summary>
    public const string MetadataHydrated = "MetadataHydrated";

    /// <summary>A remote artwork hash was verified against the local copy.</summary>
    public const string HashVerified = "HashVerified";

    /// <summary>A media file was moved to a new location in the library.</summary>
    public const string PathUpdated = "PathUpdated";

    /// <summary>A weekly (or manual) metadata sync completed.</summary>
    public const string SyncCompleted = "SyncCompleted";

    /// <summary>A library crawl (Great Inhale) was started.</summary>
    public const string CrawlStarted = "CrawlStarted";

    /// <summary>A library crawl (Great Inhale) finished.</summary>
    public const string CrawlFinished = "CrawlFinished";

    /// <summary>Metadata was refreshed for one or more entities.</summary>
    public const string MetadataRefreshed = "MetadataRefreshed";

    /// <summary>A library.xml sidecar was updated on disk.</summary>
    public const string SidecarUpdated = "SidecarUpdated";

    /// <summary>Old activity entries were pruned from the ledger.</summary>
    public const string ActivityPruned = "ActivityPruned";

    /// <summary>An external bridge identifier was synced or updated from Wikidata.</summary>
    public const string BridgeSyncUpdated = "BridgeSyncUpdated";

    /// <summary>A person entity was deeply enriched with social links and metadata.</summary>
    public const string PersonHydrated = "PersonHydrated";

    /// <summary>A weekly metadata sync cycle was started.</summary>
    public const string WeeklySyncStarted = "WeeklySyncStarted";

    /// <summary>An affiliate link was generated from a bridge identifier.</summary>
    public const string AffiliateGenerated = "AffiliateGenerated";

    // ── Hydration Pipeline (Three-Stage) ─────────────────────────────────────

    /// <summary>Hydration Stage 1 (Retail Match) completed for an entity.</summary>
    public const string HydrationStage1Completed = "HydrationStage1Completed";

    /// <summary>Hydration Stage 2 (Universal Bridge) completed for an entity.</summary>
    public const string HydrationStage2Completed = "HydrationStage2Completed";

    /// <summary>Hydration Stage 3 (Human Hub) completed for an entity.</summary>
    public const string HydrationStage3Completed = "HydrationStage3Completed";

    /// <summary>A review queue item was created (disambiguation, low confidence, etc.).</summary>
    public const string ReviewItemCreated = "ReviewItemCreated";

    /// <summary>A review queue item was resolved or dismissed by a user.</summary>
    public const string ReviewItemResolved = "ReviewItemResolved";

    /// <summary>A user manually overrode metadata fields via the Edit Metadata dialog.</summary>
    public const string MetadataManualOverride = "MetadataManualOverride";

    /// <summary>Resolved metadata was written back into the physical media file's embedded tags.</summary>
    public const string MetadataWrittenToFile = "MetadataWrittenToFile";

    // ── Ingestion Pipeline Lifecycle ─────────────────────────────────────

    /// <summary>The ingestion engine started and began watching a directory.</summary>
    public const string ServerStarted = "ServerStarted";

    /// <summary>The ingestion engine was stopped.</summary>
    public const string ServerStopped = "ServerStopped";

    /// <summary>A new file was detected in the watch folder and queued for processing.</summary>
    public const string FileDetected = "FileDetected";

    /// <summary>The SHA-256 content hash was computed for a file.</summary>
    public const string FileHashed = "FileHashed";

    /// <summary>A file was skipped because it is a duplicate of an existing asset.</summary>
    public const string DuplicateSkipped = "DuplicateSkipped";

    /// <summary>A file was processed by the processor registry (metadata extracted).</summary>
    public const string FileProcessed = "FileProcessed";

    /// <summary>The scoring engine assigned a confidence score to a file's metadata.</summary>
    public const string FileScored = "FileScored";

    /// <summary>A Hub → Work → Edition entity chain was created or linked for a file.</summary>
    public const string EntityChainCreated = "EntityChainCreated";

    /// <summary>Cover art was saved to disk alongside the organized file.</summary>
    public const string CoverArtSaved = "CoverArtSaved";

    /// <summary>Metadata tags were written back into the media file's embedded tags.</summary>
    public const string MetadataTagsWritten = "MetadataTagsWritten";

    /// <summary>A file was enqueued for external metadata enrichment via the hydration pipeline.</summary>
    public const string HydrationEnqueued = "HydrationEnqueued";

    /// <summary>A corrupt file was quarantined and excluded from further processing.</summary>
    public const string FileQuarantined = "FileQuarantined";

    /// <summary>A file was moved to the staging directory for manual review.</summary>
    public const string MovedToStaging = "MovedToStaging";

    // ── Orphan & Reconciliation ───────────────────────────────────────────

    /// <summary>An orphaned asset was cleaned up (file missing, DB record deleted).</summary>
    public const string OrphanCleaned = "OrphanCleaned";

    /// <summary>The reconciliation service found a missing file.</summary>
    public const string ReconciliationMissing = "ReconciliationMissing";

    /// <summary>A library reconciliation scan completed.</summary>
    public const string ReconciliationCompleted = "ReconciliationCompleted";

    // ── Hub Intelligence ───────────────────────────────────────────────

    /// <summary>A new Hub was created from Wikidata relationship data.</summary>
    public const string HubCreated = "HubCreated";

    /// <summary>A Work was assigned to a Hub (firm or provisional link).</summary>
    public const string HubAssigned = "HubAssigned";

    /// <summary>Two Hubs were merged when a shared relationship was discovered.</summary>
    public const string HubMerged = "HubMerged";

    // ── Consolidated Pipeline Events ──────────────────────────────────

    /// <summary>A media file was successfully ingested and enriched (end-to-end summary).</summary>
    public const string MediaAdded = "MediaAdded";

    /// <summary>Metadata was updated for an existing media item (re-hydration or re-score).</summary>
    public const string MediaUpdated = "MediaUpdated";

    /// <summary>A media file failed to ingest (corrupt, quarantined, or unprocessable).</summary>
    public const string MediaFailed = "MediaFailed";

    // ── Folder Maintenance ────────────────────────────────────────────

    /// <summary>An empty folder was cleaned up during reconciliation.</summary>
    public const string FolderCleaned = "FolderCleaned";

    /// <summary>A person folder was renamed following a metadata update (e.g. name change).</summary>
    public const string PersonFolderRenamed = "PersonFolderRenamed";
}
