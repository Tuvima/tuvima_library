namespace Tanaste.Domain.Enums;

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

    /// <summary>A tanaste.xml sidecar was updated on disk.</summary>
    public const string SidecarUpdated = "SidecarUpdated";

    /// <summary>Old activity entries were pruned from the ledger.</summary>
    public const string ActivityPruned = "ActivityPruned";
}
