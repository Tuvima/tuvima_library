using System.Text.Json.Serialization;

namespace MediaEngine.Storage.Models;

/// <summary>User preferences for the Library Vault — view modes and display options.</summary>
public sealed class VaultPreferencesSettings
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
}
