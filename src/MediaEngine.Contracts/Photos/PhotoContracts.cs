using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.Photos;

public sealed record PhotoAssetDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("file_name")] string FileName,
    [property: JsonPropertyName("captured_at")] DateTimeOffset CapturedAt,
    [property: JsonPropertyName("width")] int? Width,
    [property: JsonPropertyName("height")] int? Height,
    [property: JsonPropertyName("mime_type")] string MimeType,
    [property: JsonPropertyName("latitude")] double? Latitude,
    [property: JsonPropertyName("longitude")] double? Longitude,
    [property: JsonPropertyName("camera_make")] string? CameraMake,
    [property: JsonPropertyName("camera_model")] string? CameraModel,
    [property: JsonPropertyName("favorite")] bool Favorite,
    [property: JsonPropertyName("hidden")] bool Hidden,
    [property: JsonPropertyName("duplicate_count")] int DuplicateCount,
    [property: JsonPropertyName("thumbnail_url")] string ThumbnailUrl,
    [property: JsonPropertyName("content_url")] string ContentUrl);

public sealed record PhotoPageDto(
    [property: JsonPropertyName("items")] IReadOnlyList<PhotoAssetDto> Items,
    [property: JsonPropertyName("offset")] int Offset,
    [property: JsonPropertyName("limit")] int Limit,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("has_more")] bool HasMore);

public sealed record PhotoScanResultDto(
    [property: JsonPropertyName("files_seen")] int FilesSeen,
    [property: JsonPropertyName("photos_added")] int PhotosAdded,
    [property: JsonPropertyName("sources_added")] int SourcesAdded,
    [property: JsonPropertyName("duplicates_found")] int DuplicatesFound,
    [property: JsonPropertyName("errors")] int Errors);

public sealed record ScanPhotoLibrariesRequest;

public sealed record PhotoAlbumDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("item_count")] int ItemCount,
    [property: JsonPropertyName("cover_thumbnail_url")] string? CoverThumbnailUrl,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

public sealed record CreatePhotoAlbumRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description = null);

public sealed record SetPhotoFlagRequest(
    [property: JsonPropertyName("value")] bool Value);

public sealed record AddPhotoAlbumItemsRequest(
    [property: JsonPropertyName("photo_ids")] IReadOnlyList<Guid> PhotoIds);

public sealed record AddPhotoAlbumItemsResult(
    [property: JsonPropertyName("added")] int Added);
