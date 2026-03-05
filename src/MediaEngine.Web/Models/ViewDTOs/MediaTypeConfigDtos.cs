using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Dashboard DTO for the complete media type configuration.
/// Mirrors <c>MediaEngine.Storage.Models.MediaTypeConfiguration</c>.
/// </summary>
public sealed class MediaTypeConfigurationDto
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("types")]
    public List<MediaTypeDefinitionDto> Types { get; set; } = [];

    /// <summary>Compiled defaults used when the Engine is unreachable.</summary>
    public static List<MediaTypeDefinitionDto> Defaults() =>
    [
        new() { Key = "books",      DisplayName = "Books",      Icon = "MenuBook",    Extensions = [".epub"],                                      CategoryFolder = "Books",    BuiltIn = true },
        new() { Key = "audiobooks", DisplayName = "Audiobooks", Icon = "Headphones",  Extensions = [".m4b", ".mp3", ".m4a"],                       CategoryFolder = "Audio",    BuiltIn = true },
        new() { Key = "comics",     DisplayName = "Comics",     Icon = "AutoStories", Extensions = [".cbz", ".cbr"],                               CategoryFolder = "Comics",   BuiltIn = true },
        new() { Key = "movies",     DisplayName = "Movies",     Icon = "Movie",       Extensions = [".mp4", ".mkv", ".webm", ".avi", ".m4v"],      CategoryFolder = "Videos",   BuiltIn = true },
        new() { Key = "tv_shows",   DisplayName = "TV Shows",   Icon = "Tv",          Extensions = [".mp4", ".mkv"],                               CategoryFolder = "TV Shows", BuiltIn = true },
        new() { Key = "music",      DisplayName = "Music",      Icon = "MusicNote",   Extensions = [".mp3", ".flac", ".ogg", ".m4a", ".aac", ".wav"], CategoryFolder = "Music", BuiltIn = true },
        new() { Key = "podcasts",   DisplayName = "Podcasts",   Icon = "Podcasts",    Extensions = [".mp3", ".m4a"],                               CategoryFolder = "Podcasts", BuiltIn = true },
    ];
}

/// <summary>
/// Dashboard DTO for a single media type definition.
/// Mirrors <c>MediaEngine.Storage.Models.MediaTypeDefinition</c>.
/// </summary>
public sealed class MediaTypeDefinitionDto
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("icon")]
    public string Icon { get; set; } = "Description";

    [JsonPropertyName("extensions")]
    public List<string> Extensions { get; set; } = [];

    [JsonPropertyName("category_folder")]
    public string CategoryFolder { get; set; } = "Other";

    [JsonPropertyName("built_in")]
    public bool BuiltIn { get; set; }
}
