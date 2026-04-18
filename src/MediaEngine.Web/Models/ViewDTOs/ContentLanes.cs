using MudBlazor;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Static registry of category navigation lanes.
/// Three content categories: Read (Books+Comics), Watch (Movies+TV), Listen (Music+Audiobooks).
/// </summary>
public static class ContentLanes
{
    public static readonly LaneDefinition Home = new(
        "home", "Home",
        Icons.Material.Outlined.Home,
        [],
        []);

    public static readonly LaneDefinition Read = new(
        "read", "Read",
        Icons.Material.Filled.MenuBook,
        ["Books", "Audiobooks", "Comics"],
        ["All", "Books", "Comics", "Audiobooks"]);

    public static readonly LaneDefinition Watch = new(
        "watch", "Watch",
        Icons.Material.Filled.Movie,
        ["Movies", "TV"],
        ["All", "Movies", "TV Shows"]);

    public static readonly LaneDefinition Listen = new(
        "listen", "Listen",
        Icons.Material.Filled.Headphones,
        ["Music", "Audiobooks"],
        ["All", "Music", "Audiobooks"]);

    /// <summary>All navigation lanes in display order.</summary>
    public static readonly IReadOnlyList<LaneDefinition> All =
        [Home, Read, Watch, Listen];

    /// <summary>Content lanes only (excludes Home).</summary>
    public static readonly IReadOnlyList<LaneDefinition> ContentOnly =
        [Read, Watch, Listen];

    /// <summary>Finds a lane by its URL key (case-insensitive).</summary>
    public static LaneDefinition? ByKey(string key) =>
        All.FirstOrDefault(l => l.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Returns the accent colour for a lane.
    /// </summary>
    public static string AccentColor(LaneDefinition lane) => lane.Key switch
    {
        "read"   => "#5DCAA5", // Green — reading
        "watch"  => "#60A5FA", // Blue — watching
        "listen" => "#1ED760", // Green — listening
        _        => "#C9922E", // Golden amber — brand default
    };
}
