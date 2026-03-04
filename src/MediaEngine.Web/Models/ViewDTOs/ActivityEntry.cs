namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// A single entry in the activity timeline — a plain-English description
/// of something that happened in the library (new media, metadata update,
/// server event, etc.).
/// </summary>
public sealed record ActivityEntry(
    DateTimeOffset  OccurredAt,
    ActivityKind    Kind,
    string          Icon,
    string          Summary,
    string?         Detail = null);

/// <summary>Categories for activity timeline entries.</summary>
public enum ActivityKind
{
    MediaAdded,
    MetadataUpdated,
    PersonEnriched,
    IngestionProgress,
    ServerStatus,
    WatchFolderChanged,
    Error,
}
