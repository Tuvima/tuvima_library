using System.Text.Json.Serialization;

namespace MediaEngine.Storage.Models;

/// <summary>
/// Provider slot assignment for a single media type.
///
/// Each media type has three slots:
/// <list type="bullet">
///   <item><see cref="Primary"/> — runs automatically during Stage 1 (Content Match).</item>
///   <item><see cref="Secondary"/> — available as Manual Fallback 1 via the search API.</item>
///   <item><see cref="Tertiary"/> — available as Manual Fallback 2 via the search API.</item>
/// </list>
///
/// Only the Primary provider runs automatically. Secondary and Tertiary are reserved
/// for manual querying via the Needs Review search interface.
/// </summary>
public sealed class ProviderSlotConfig
{
    /// <summary>
    /// Provider name for the automatic match slot.
    /// This provider runs during Stage 1 whenever a file of this media type is ingested.
    /// <c>null</c> means no automatic matching for this media type.
    /// </summary>
    [JsonPropertyName("primary")]
    public string? Primary { get; set; }

    /// <summary>
    /// Provider name for the first manual fallback slot.
    /// Available in the Needs Review search dropdown when the primary match fails.
    /// </summary>
    [JsonPropertyName("secondary")]
    public string? Secondary { get; set; }

    /// <summary>
    /// Provider name for the second manual fallback slot.
    /// Available in the Needs Review search dropdown when the primary match fails.
    /// </summary>
    [JsonPropertyName("tertiary")]
    public string? Tertiary { get; set; }
}

/// <summary>
/// Complete provider slot configuration for all media types.
///
/// Loaded from <c>config/slots.json</c>. Maps media type display names
/// (e.g. "Books", "Audiobooks") to their three-slot provider assignments.
///
/// The pipeline reads this to determine which provider to use for automatic
/// Stage 1 matching. The Dashboard reads this to populate the drag-drop
/// slot interface.
/// </summary>
public sealed class ProviderSlotConfiguration
{
    /// <summary>
    /// Maps media type display names to their slot assignments.
    /// Keys: "Books", "Audiobooks", "Comics", "Movies", "TV Shows".
    /// </summary>
    [JsonPropertyName("slots")]
    public Dictionary<string, ProviderSlotConfig> Slots { get; set; } = new()
    {
        ["Books"]      = new() { Primary = "apple_books",  Secondary = "google_books",  Tertiary = "open_library" },
        ["Audiobooks"] = new() { Primary = "apple_books", Secondary = "google_books",  Tertiary = null },
        ["Comics"]     = new() { Primary = null,                  Secondary = null,                    Tertiary = null },
        ["Movies"]     = new() { Primary = null,                  Secondary = null,                    Tertiary = null },
        ["TV Shows"]   = new() { Primary = null,                  Secondary = null,                    Tertiary = null },
    };

    /// <summary>
    /// Resolves the slot config for a given media type display name.
    /// Returns an empty slot config if the media type is not mapped.
    /// </summary>
    public ProviderSlotConfig GetSlotForMediaType(string mediaTypeDisplayName)
    {
        return Slots.TryGetValue(mediaTypeDisplayName, out var slot) ? slot : new();
    }

    /// <summary>
    /// Maps a <see cref="Domain.Enums.MediaType"/> enum value to its display name
    /// used as the key in <see cref="Slots"/>.
    /// </summary>
    public static string MediaTypeToDisplayName(Domain.Enums.MediaType mediaType) => mediaType switch
    {
        Domain.Enums.MediaType.Books      => "Books",
        Domain.Enums.MediaType.Audiobooks => "Audiobooks",
        Domain.Enums.MediaType.Comics     => "Comics",
        Domain.Enums.MediaType.Movies     => "Movies",
        Domain.Enums.MediaType.TV         => "TV Shows",
        Domain.Enums.MediaType.Music     => "Music",
        Domain.Enums.MediaType.Podcasts  => "Podcasts",
        _                                => "Books", // Default fallback
    };
}
