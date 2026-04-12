namespace MediaEngine.Domain.Entities;

/// <summary>
/// Junction entity linking a Collection to a Work for curated membership.
/// Used by System Lists, Playlists, and Personalised Mixes — collections where
/// items are explicitly added (not derived from works.collection_id like Series/Universe collections).
///
/// Maps to <c>collection_items</c> table.
/// </summary>
public sealed class CollectionItem
{
    public Guid Id { get; set; }
    public Guid CollectionId { get; set; }
    public Guid WorkId { get; set; }
    public int SortOrder { get; set; }

    /// <summary>"not_started", "in_progress", or "completed".</summary>
    public string ProgressState { get; set; } = "not_started";

    /// <summary>Freeform position marker (page number, timestamp, episode number).</summary>
    public string? ProgressPosition { get; set; }

    public DateTimeOffset AddedAt { get; set; }
}
