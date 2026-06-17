using System.Text.Json.Serialization;

namespace MediaEngine.Storage.Models;

/// <summary>User preferences for the library — view modes and display options.</summary>
public sealed class LibraryLaneGroupDisplaySettings
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    [JsonPropertyName("shelf_key")]
    public string? ShelfKey { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; set; }

    [JsonPropertyName("see_all_route")]
    public string? SeeAllRoute { get; set; }

    [JsonPropertyName("minimum_series_items")]
    public int? MinimumSeriesItems { get; set; }
}

public sealed class LibraryPreferencesSettings
{
    /// <summary>Whether to show unowned items (episodes/tracks not in library) in container drill-down views. Default: true.</summary>
    [JsonPropertyName("show_unowned")]
    public bool ShowUnowned { get; set; } = true;

    /// <summary>Per-tab view mode preferences. Keys are tab IDs, values are view mode strings.</summary>
    [JsonPropertyName("view_modes")]
    public Dictionary<string, string> ViewModes { get; set; } = new()
    {
        ["movies"] = "all",
        ["tv"] = "shows",
        ["music"] = "artists",
        ["books"] = "all",
        ["audiobooks"] = "all",

        ["comics"] = "series",
    };

    /// <summary>Configurable display rules for lane-level grouping shelves.</summary>
    [JsonPropertyName("lane_group_display")]
    public Dictionary<string, LibraryLaneGroupDisplaySettings> LaneGroupDisplay { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["watch"] = new()
        {
            Enabled = true,
            ShelfKey = "shows-and-series",
            Title = "Shows & Series",
            Subtitle = "TV shows and film series grouped by title",
            SeeAllRoute = "/watch/tv",
            MinimumSeriesItems = 2,
        },
        ["read"] = new()
        {
            Enabled = true,
            ShelfKey = "series-and-reading-lists",
            Title = "Series & Reading Lists",
            Subtitle = "Book series, comic runs, and grouped reading",
            SeeAllRoute = "/read/books?grouping=series",
            MinimumSeriesItems = 2,
        },
    };
}

