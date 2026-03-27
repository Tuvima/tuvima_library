namespace MediaEngine.Domain.Entities;

/// <summary>
/// Junction entity linking a Hub to a Work for curated membership.
/// Used by System Lists, Playlists, and Personalised Mixes — hubs where
/// items are explicitly added (not derived from works.hub_id like Series/Universe hubs).
///
/// Maps to <c>hub_items</c> table.
/// </summary>
public sealed class HubItem
{
    public Guid Id { get; set; }
    public Guid HubId { get; set; }
    public Guid WorkId { get; set; }
    public int SortOrder { get; set; }

    /// <summary>"not_started", "in_progress", or "completed".</summary>
    public string ProgressState { get; set; } = "not_started";

    /// <summary>Freeform position marker (page number, timestamp, episode number).</summary>
    public string? ProgressPosition { get; set; }

    public DateTimeOffset AddedAt { get; set; }
}
