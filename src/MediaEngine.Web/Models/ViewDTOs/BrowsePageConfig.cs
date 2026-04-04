using MediaEngine.Domain.Services;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Configuration for the home page (LibraryBrowsePage).
/// Category browse pages (Read/Watch/Listen) use their own inline config.
/// </summary>
public sealed record BrowsePageConfig
{
    public required string PageTitle { get; init; }
    public string? GreetingOverride { get; init; }
    public IReadOnlyList<string> MediaTypes { get; init; } = [];
    public bool HeroEnabled { get; init; } = true;
    public CardVariant DefaultCardVariant { get; init; } = CardVariant.Portrait;
    public bool GridModeAvailable { get; init; }
    public IReadOnlyList<SwimlaneDef> Swimlanes { get; init; } = [];
    public LaneDefinition? Lane { get; init; }
    public string AccentColor { get; init; } = "#C9922E";
    public string ContinueLabel { get; init; } = "Pick Up Where You Left Off";
    public string? StatLabel { get; init; }

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
            new SwimlaneDef(SwimlaneType.Explore, "Explore Your Library", CardVariant.Portrait),
        ],
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

public sealed record SwimlaneDef(SwimlaneType Type, string TitleTemplate, CardVariant CardVariant);
