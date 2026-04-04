using MediaEngine.Domain.Services;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Configuration for the unified LibraryBrowsePage.
/// Each page instance (Home, Books, Video, Music, etc.) provides its own config.
/// </summary>
public sealed record BrowsePageConfig
{
    /// <summary>Page title shown in browser tab.</summary>
    public required string PageTitle { get; init; }

    /// <summary>Greeting prefix — e.g. "Your Books" or null for Home (uses time-of-day).</summary>
    public string? GreetingOverride { get; init; }

    /// <summary>Media types to include. Empty = all types (Home page).</summary>
    public IReadOnlyList<string> MediaTypes { get; init; } = [];

    /// <summary>Whether to show the compact hero section.</summary>
    public bool HeroEnabled { get; init; } = true;

    /// <summary>Default card variant for browse swimlanes.</summary>
    public CardVariant DefaultCardVariant { get; init; } = CardVariant.Portrait;

    /// <summary>Whether the grid view toggle is available.</summary>
    public bool GridModeAvailable { get; init; }

    /// <summary>Swimlane plan — ordered list of swimlane definitions to render.</summary>
    public IReadOnlyList<SwimlaneDef> Swimlanes { get; init; } = [];

    /// <summary>Lane definition for sidebar active state. Null for Home.</summary>
    public LaneDefinition? Lane { get; init; }

    /// <summary>Accent colour for this page context.</summary>
    public string AccentColor { get; init; } = "#C9922E";

    /// <summary>Label for the continue swimlane (media-type aware).</summary>
    public string ContinueLabel { get; init; } = "Pick Up Where You Left Off";

    /// <summary>Library stat label — e.g. "342 books" or "1,247 items".</summary>
    public string? StatLabel { get; init; }

    // ── Factory methods ─────────────────────────────────────────────

    public static BrowsePageConfig ForHome() => new()
    {
        PageTitle = "Tuvima Library",
        HeroEnabled = true,
        DefaultCardVariant = CardVariant.Portrait,
        ContinueLabel = "Pick Up Where You Left Off",
        Swimlanes =
        [
            new SwimlaneDef(SwimlaneType.Continue, "Pick Up Where You Left Off", CardVariant.Landscape),
            new SwimlaneDef(SwimlaneType.RecentlyAdded, "Recently Added", CardVariant.Portrait),
            new SwimlaneDef(SwimlaneType.UniverseSpotlight, "Universe Spotlight", CardVariant.Wide),
            new SwimlaneDef(SwimlaneType.BecauseYou, "Because You {Verb} {Title}", CardVariant.Portrait),
            new SwimlaneDef(SwimlaneType.GenreSuggestion, "{Genre} You Might Like", CardVariant.Portrait),
            new SwimlaneDef(SwimlaneType.Unfinished, "Worth Another Look", CardVariant.Landscape),
            new SwimlaneDef(SwimlaneType.Explore, "Explore Your Library", CardVariant.Portrait),
        ],
    };

    public static BrowsePageConfig ForBooks() => new()
    {
        PageTitle = "Books — Tuvima",
        GreetingOverride = "Your Books",
        MediaTypes = ["Books", "Audiobooks"],
        HeroEnabled = true,
        DefaultCardVariant = CardVariant.Portrait,
        ContinueLabel = "Continue Reading",
        AccentColor = "#FF8F00",
        Lane = ContentLanes.Books,
        Swimlanes =
        [
            new SwimlaneDef(SwimlaneType.Continue, "Continue Reading", CardVariant.Landscape),
            new SwimlaneDef(SwimlaneType.RecentlyAdded, "Recently Added", CardVariant.Portrait),
            new SwimlaneDef(SwimlaneType.FeaturedPersons, "Authors You Follow", CardVariant.Portrait),
            new SwimlaneDef(SwimlaneType.SeriesGroups, "By Series", CardVariant.Portrait),
            new SwimlaneDef(SwimlaneType.GenreGroups, "By Genre", CardVariant.Portrait),
            new SwimlaneDef(SwimlaneType.Explore, "Explore Your Books", CardVariant.Portrait),
        ],
    };

    public static BrowsePageConfig ForVideo() => new()
    {
        PageTitle = "Video — Tuvima",
        GreetingOverride = "Your Videos",
        MediaTypes = ["Movies", "TV"],
        HeroEnabled = true,
        DefaultCardVariant = CardVariant.Portrait,
        ContinueLabel = "Continue Watching",
        AccentColor = "#00BFA5",
        Lane = ContentLanes.Video,
        Swimlanes =
        [
            new SwimlaneDef(SwimlaneType.Continue, "Continue Watching", CardVariant.Landscape),
            new SwimlaneDef(SwimlaneType.RecentlyAdded, "Recently Added", CardVariant.Portrait),
            new SwimlaneDef(SwimlaneType.FeaturedPersons, "Directors", CardVariant.Portrait),
            new SwimlaneDef(SwimlaneType.GenreGroups, "By Genre", CardVariant.Portrait),
            new SwimlaneDef(SwimlaneType.Explore, "Explore Your Videos", CardVariant.Portrait),
        ],
    };

    public static BrowsePageConfig ForMusic() => new()
    {
        PageTitle = "Music — Tuvima",
        GreetingOverride = "Your Music",
        MediaTypes = ["Music"],
        HeroEnabled = true,
        DefaultCardVariant = CardVariant.Square,
        ContinueLabel = "Recently Played",
        AccentColor = "#EC407A",
        GridModeAvailable = true,
        Lane = ContentLanes.Music,
        Swimlanes =
        [
            new SwimlaneDef(SwimlaneType.Continue, "Recently Played", CardVariant.Landscape),
            new SwimlaneDef(SwimlaneType.RecentlyAdded, "New in Your Library", CardVariant.Square),
            new SwimlaneDef(SwimlaneType.FeaturedPersons, "Artists", CardVariant.Square),
            new SwimlaneDef(SwimlaneType.GenreGroups, "By Genre", CardVariant.Square),
            new SwimlaneDef(SwimlaneType.Explore, "Explore Your Music", CardVariant.Square),
        ],
    };

    public static BrowsePageConfig ForPodcasts() => new()
    {
        PageTitle = "Podcasts — Tuvima",
        GreetingOverride = "Your Podcasts",
        MediaTypes = ["Podcasts"],
        HeroEnabled = true,
        DefaultCardVariant = CardVariant.Square,
        ContinueLabel = "Continue Listening",
        AccentColor = "#AB47BC",
        Lane = ContentLanes.Podcasts,
        Swimlanes =
        [
            new SwimlaneDef(SwimlaneType.Continue, "Continue Listening", CardVariant.Landscape),
            new SwimlaneDef(SwimlaneType.RecentlyAdded, "New Episodes", CardVariant.Square),
            new SwimlaneDef(SwimlaneType.SeriesGroups, "Shows", CardVariant.Square),
            new SwimlaneDef(SwimlaneType.Explore, "Discover Podcasts", CardVariant.Square),
        ],
    };

    public static BrowsePageConfig ForComics() => new()
    {
        PageTitle = "Comics — Tuvima",
        GreetingOverride = "Your Comics",
        MediaTypes = ["Comics"],
        HeroEnabled = true,
        DefaultCardVariant = CardVariant.Portrait,
        ContinueLabel = "Continue Reading",
        AccentColor = "#7C4DFF",
        Lane = ContentLanes.Comics,
        Swimlanes =
        [
            new SwimlaneDef(SwimlaneType.Continue, "Continue Reading", CardVariant.Landscape),
            new SwimlaneDef(SwimlaneType.RecentlyAdded, "Recently Added", CardVariant.Portrait),
            new SwimlaneDef(SwimlaneType.SeriesGroups, "By Series", CardVariant.Portrait),
            new SwimlaneDef(SwimlaneType.Explore, "Explore Your Comics", CardVariant.Portrait),
        ],
    };

    /// <summary>Resolve config from URL path key.</summary>
    public static BrowsePageConfig ForLane(string key) => key switch
    {
        "books" => ForBooks(),
        "video" => ForVideo(),
        "music" => ForMusic(),
        "podcasts" => ForPodcasts(),
        "comics" => ForComics(),
        _ => ForHome(),
    };
}

public enum CardVariant { Portrait, Landscape, Square, Wide }

public enum SwimlaneType
{
    Continue,
    RecentlyAdded,
    UniverseSpotlight,
    BecauseYou,
    GenreSuggestion,
    FeaturedPersons,
    SeriesGroups,
    GenreGroups,
    Unfinished,
    Explore,
}

/// <summary>Defines a single swimlane slot in a page configuration.</summary>
public sealed record SwimlaneDef(SwimlaneType Type, string TitleTemplate, CardVariant CardVariant);
