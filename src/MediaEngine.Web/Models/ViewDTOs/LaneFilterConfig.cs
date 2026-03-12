namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Per-lane configuration that drives which filter controls appear in the filter bar
/// and how smart section headers are labelled.
/// </summary>
public sealed record LaneFilterConfig(
    string[] FormatOptions,
    string PersonLabel,
    string? SeriesLabel,
    bool ShowYearRange,
    string GroupByLabel,
    string ItemNoun)
{
    public static LaneFilterConfig ForLane(string laneKey) => laneKey switch
    {
        "books"    => new(["All", "eBook", "Audiobook"], "Author", "Series", true, "Author", "book"),
        "video"    => new(["All", "Movie", "TV Show"], "Director", "Franchise", true, "Director", "title"),
        "comics"   => new(["All"], "Artist", "Series", true, "Artist", "comic"),
        "music"    => new(["All"], "Artist", null, true, "Artist", "album"),
        "podcasts" => new(["All"], "Host", null, false, "Host", "podcast"),
        _          => new(["All"], "Creator", null, true, "Creator", "item"),
    };
}
