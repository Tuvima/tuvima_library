using System.Text.Json.Serialization;
using System.Text.Json;
using MediaEngine.Contracts.Collections;
using MediaEngine.Domain.Services;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Dashboard route model layered over the shared collection-group response contract.
/// </summary>
public sealed class CollectionGroupDetailViewModel : CollectionGroupDetailDto
{
    public static CollectionGroupDetailViewModel FromContract(CollectionGroupDetailDto source) =>
        JsonSerializer.Deserialize<CollectionGroupDetailViewModel>(
            JsonSerializer.Serialize(source, MediaEngineJson.Web),
            MediaEngineJson.Web)
        ?? throw new InvalidOperationException("Could not map collection group detail for presentation.");
}

/// <summary>
/// Dashboard display behavior layered over the shared content-group response contract.
/// </summary>
public sealed class ContentGroupViewModel : ContentGroupDto
{
    public static ContentGroupViewModel FromContract(ContentGroupDto source) =>
        JsonSerializer.Deserialize<ContentGroupViewModel>(
            JsonSerializer.Serialize(source, MediaEngineJson.Web),
            MediaEngineJson.Web)
        ?? throw new InvalidOperationException("Could not map content group for presentation.");

    [JsonIgnore]
    public string MediaTypeIcon => PrimaryMediaType switch
    {
        "TV" => "LiveTv",
        "Music" => "MusicNote",
        "Books" => "MenuBook",
        "Audiobooks" => "Headphones",
        "Movies" => "VideoLibrary",
        "Comics" => "AutoStories",
        _ => "Folder",
    };

    [JsonIgnore]
    public string MediaTypeColor => PrimaryMediaType switch
    {
        "TV" => "var(--tl-media-video)",
        "Music" => "#1ED760",
        "Books" => "var(--tl-status-success)",
        "Audiobooks" => "#84CC16",
        "Movies" => "var(--tl-status-info)",
        "Comics" => "var(--tl-media-comic)",
        _ => "var(--tl-status-info)",
    };
}
