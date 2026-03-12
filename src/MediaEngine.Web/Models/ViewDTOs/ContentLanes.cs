using MudBlazor;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Static registry of all content-type navigation lanes.
/// Replaces the user-configurable Virtual Library system with fixed,
/// Netflix-style content lanes that always appear in the dock.
/// </summary>
public static class ContentLanes
{
    public static readonly LaneDefinition Home = new(
        "home", "Home",
        Icons.Material.Outlined.Home,
        [],
        ["Continue Journey", "Recently Added", "Smart Collections"]);

    public static readonly LaneDefinition Search = new(
        "search", "Search",
        Icons.Material.Filled.Search,
        [],
        []);

    public static readonly LaneDefinition Books = new(
        "books", "Books",
        Icons.Material.Filled.MenuBook,
        ["Epub", "Audiobook", "Book", "M4B"],
        ["All", "Reading Lists", "Authors", "Series"]);

    public static readonly LaneDefinition Video = new(
        "video", "Video",
        Icons.Material.Filled.Movie,
        ["Movie", "TV", "Video"],
        ["All", "Watchlists", "Directors", "Genres"]);

    public static readonly LaneDefinition Music = new(
        "music", "Music",
        Icons.Material.Filled.MusicNote,
        ["Music"],
        ["All", "Playlists", "Artists", "Genres"]);

    public static readonly LaneDefinition Podcasts = new(
        "podcasts", "Podcasts",
        Icons.Material.Filled.Podcasts,
        ["Podcast"],
        ["All", "Subscriptions", "Episodes"]);

    public static readonly LaneDefinition Comics = new(
        "comics", "Comics",
        Icons.Material.Filled.AutoStories,
        ["Comic"],
        ["All", "Reading Lists", "Series", "Publishers"]);

    /// <summary>All navigation lanes in dock display order.</summary>
    public static readonly IReadOnlyList<LaneDefinition> All =
        [Home, Search, Books, Video, Music, Podcasts, Comics];

    /// <summary>Content lanes only (excludes Home and Search).</summary>
    public static readonly IReadOnlyList<LaneDefinition> ContentOnly =
        [Books, Video, Music, Podcasts, Comics];

    /// <summary>Finds a lane by its URL key (case-insensitive).</summary>
    public static LaneDefinition? ByKey(string key) =>
        All.FirstOrDefault(l => l.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Returns the accent colour for a lane based on its primary media type bucket.
    /// </summary>
    public static string AccentColor(LaneDefinition lane) => lane.Key switch
    {
        "books"    => "#FF8F00", // Amber — warm, literary
        "video"    => "#00BFA5", // Teal — cinematic
        "music"    => "#EC407A", // Rose — vibrant audio
        "podcasts" => "#AB47BC", // Purple — spoken word
        "comics"   => "#7C4DFF", // Violet — illustration
        _          => "#C9922E", // Golden amber — brand default
    };
}
