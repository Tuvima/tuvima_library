using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.Settings;

public sealed class LibraryPreferencesSettings
{
    [JsonPropertyName("show_unowned")]
    public bool ShowUnowned { get; set; } = true;

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
