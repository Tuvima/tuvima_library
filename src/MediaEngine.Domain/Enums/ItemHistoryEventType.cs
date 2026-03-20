namespace MediaEngine.Domain.Enums;

/// <summary>
/// Event types for the per-item history timeline.
/// Each constant represents a step in the item's lifecycle.
/// </summary>
public static class ItemHistoryEventType
{
    public const string FileDetected = "FileDetected";
    public const string MetadataExtracted = "MetadataExtracted";
    public const string ConfidenceScored = "ConfidenceScored";
    public const string WikidataMatched = "WikidataMatched";
    public const string WikidataMatchFailed = "WikidataMatchFailed";
    public const string CoverArtFound = "CoverArtFound";
    public const string CoverArtNotFound = "CoverArtNotFound";
    public const string PersonLinked = "PersonLinked";
    public const string Staged = "Staged";
    public const string Promoted = "Promoted";
    public const string MetadataUpdated = "MetadataUpdated";
    public const string UserEdited = "UserEdited";
    public const string Rejected = "Rejected";
    public const string Recovered = "Recovered";
    public const string ReviewCreated = "ReviewCreated";
    public const string ReviewResolved = "ReviewResolved";
    public const string HydrationStarted = "HydrationStarted";
    public const string HydrationCompleted = "HydrationCompleted";
    public const string RetailEnriched = "RetailEnriched";
    public const string RetailEnrichFailed = "RetailEnrichFailed";
}
