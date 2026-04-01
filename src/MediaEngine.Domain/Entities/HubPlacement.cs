namespace MediaEngine.Domain.Entities;

/// <summary>
/// Maps a Hub to a UI location with display constraints.
/// A hub can appear in multiple locations (home, media lane, vault) with different limits.
/// Maps to <c>hub_placements</c> table.
/// </summary>
public sealed class HubPlacement
{
    public Guid Id { get; set; }
    public Guid HubId { get; set; }

    /// <summary>UI location: "home", "vault:tv", "media_lane:Movie", "hubs", "my_library", etc.</summary>
    public string Location { get; set; } = "";

    /// <summary>Sort order within the location (lower = higher on page).</summary>
    public int Position { get; set; }

    /// <summary>Max items to display at this location (0 = no limit).</summary>
    public int DisplayLimit { get; set; }

    /// <summary>How to render: "swimlane", "grid", "list", "accordion", "container_table".</summary>
    public string DisplayMode { get; set; } = "swimlane";

    /// <summary>User can hide a placement without deleting it.</summary>
    public bool IsVisible { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
}
