using System.Text.Json;
using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Root navigation configuration stored per-profile as a JSON blob.
/// Contains the header action cluster and bottom tray layout preferences.
/// The Engine stores this as an opaque string; the Dashboard interprets it.
/// </summary>
public sealed class NavigationConfigViewModel
{
    [JsonPropertyName("actionCluster")]
    public ActionClusterConfig ActionCluster { get; set; } = new();

    [JsonPropertyName("tray")]
    public TrayConfig Tray { get; set; } = new();
}

/// <summary>
/// Configuration for the header's right-side action cluster.
/// Each item is an icon button that can be individually enabled/disabled.
/// </summary>
public sealed class ActionClusterConfig
{
    [JsonPropertyName("items")]
    public List<ActionClusterItem> Items { get; set; } = [];
}

/// <summary>A single action button in the header's action cluster.</summary>
public sealed class ActionClusterItem
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Configuration for the bottom navigation tray.
/// Contains an ordered list of Virtual Libraries that filter the Bento Grid.
/// </summary>
public sealed class TrayConfig
{
    [JsonPropertyName("libraries")]
    public List<VirtualLibrary> Libraries { get; set; } = [];
}

/// <summary>
/// A Virtual Library maps a user-facing label to one or more <c>MediaType</c> enum values.
/// Clicking a library button in the tray filters the Bento Grid using OR logic across
/// all mapped media types. An empty <see cref="MediaTypes"/> list means "show all" (no filter).
/// </summary>
public sealed class VirtualLibrary
{
    /// <summary>Stable unique key for this library (e.g., "tv", "movies", "books").</summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>User-facing display label (e.g., "TV", "Movies", "Books").</summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    /// <summary>Material Design icon name (e.g., "LiveTv", "Movie", "MenuBook").</summary>
    [JsonPropertyName("icon")]
    public string Icon { get; set; } = string.Empty;

    /// <summary>
    /// Media types this library includes using OR logic.
    /// Values must match <c>MediaType</c> enum names (e.g., "Movies", "TV", "Books").
    /// An empty list means "show all" — no filtering applied.
    /// </summary>
    [JsonPropertyName("mediaTypes")]
    public List<string> MediaTypes { get; set; } = [];

    /// <summary>Whether this library is visible in the tray.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Provides default navigation configuration and JSON serialisation helpers.
/// </summary>
public static class NavigationDefaults
{
    /// <summary>Material icon name → MudBlazor icon string mapping.</summary>
    public static string GetMudIcon(string iconName) => iconName switch
    {
        "LiveTv"      => MudBlazor.Icons.Material.Filled.LiveTv,
        "Movie"       => MudBlazor.Icons.Material.Filled.Movie,
        "MenuBook"    => MudBlazor.Icons.Material.Filled.MenuBook,
        "Headphones"  => MudBlazor.Icons.Material.Filled.Headphones,
        "AutoStories" => MudBlazor.Icons.Material.Filled.AutoStories,
        "Category"    => MudBlazor.Icons.Material.Filled.Category,
        "Cast"        => MudBlazor.Icons.Material.Filled.Cast,
        "Person"      => MudBlazor.Icons.Material.Filled.Person,
        "MusicNote"   => MudBlazor.Icons.Material.Filled.MusicNote,
        "Podcasts"    => MudBlazor.Icons.Material.Filled.Podcasts,
        _             => MudBlazor.Icons.Material.Filled.Folder,
    };

    /// <summary>
    /// Media type colour palette for tray buttons — mirrors <c>UniverseMapper</c>.
    /// </summary>
    public static string GetMediaTypeColor(string iconName) => iconName switch
    {
        "LiveTv"      => "#00BFA5",
        "Movie"       => "#00BFA5",
        "MenuBook"    => "#FF8F00",
        "Headphones"  => "#EC407A",
        "AutoStories" => "#7C4DFF",
        "Category"    => "#9E9E9E",
        "MusicNote"   => "#EC407A",
        "Podcasts"    => "#EC407A",
        _             => "#9E9E9E",
    };

    /// <summary>
    /// Default navigation configuration applied when a profile has no custom config.
    /// </summary>
    public static NavigationConfigViewModel CreateDefault() => new()
    {
        ActionCluster = new ActionClusterConfig
        {
            Items =
            [
                new ActionClusterItem { Key = "cast",    Enabled = true },
                new ActionClusterItem { Key = "profile", Enabled = true },
            ],
        },
        Tray = new TrayConfig
        {
            Libraries =
            [
                new VirtualLibrary { Key = "tv",         Label = "TV",         Icon = "LiveTv",      MediaTypes = ["TV"],         Enabled = true },
                new VirtualLibrary { Key = "movies",     Label = "Movies",     Icon = "Movie",       MediaTypes = ["Movies"],     Enabled = true },
                new VirtualLibrary { Key = "books",      Label = "Books",      Icon = "MenuBook",    MediaTypes = ["Books"],      Enabled = true },
                new VirtualLibrary { Key = "audiobooks", Label = "Audiobooks", Icon = "Headphones",  MediaTypes = ["Audiobooks"], Enabled = true },
                new VirtualLibrary { Key = "comics",     Label = "Comics",     Icon = "AutoStories", MediaTypes = ["Comic"],      Enabled = true },
                new VirtualLibrary { Key = "hubs",       Label = "Hubs",       Icon = "Category",    MediaTypes = [],             Enabled = true },
            ],
        },
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Deserialises a navigation config from a profile's JSON string.
    /// Returns defaults if the string is null, empty, or invalid JSON.
    /// </summary>
    public static NavigationConfigViewModel Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return CreateDefault();

        try
        {
            return JsonSerializer.Deserialize<NavigationConfigViewModel>(json, JsonOptions)
                   ?? CreateDefault();
        }
        catch
        {
            return CreateDefault();
        }
    }

    /// <summary>
    /// Serialises a navigation config to a JSON string for storage in the profile.
    /// </summary>
    public static string Serialize(NavigationConfigViewModel config)
        => JsonSerializer.Serialize(config, JsonOptions);
}
