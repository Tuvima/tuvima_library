using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.LocalAssets;

/// <summary>
/// A library-scoped local item. Unlike a catalogue work, this identity is made
/// from files owned or referenced by a personal library and is never enriched
/// through an external identity provider.
/// </summary>
public sealed record LocalAssetDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("library_id")] Guid LibraryId,
    [property: JsonPropertyName("personal_space_id")] Guid PersonalSpaceId,
    [property: JsonPropertyName("owner_profile_id")] Guid OwnerProfileId,
    [property: JsonPropertyName("media_kind")] string MediaKind,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("file_name")] string FileName,
    [property: JsonPropertyName("mime_type")] string MimeType,
    [property: JsonPropertyName("captured_at")] DateTimeOffset? CapturedAt,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("width")] int? Width,
    [property: JsonPropertyName("height")] int? Height,
    [property: JsonPropertyName("duration_seconds")] double? DurationSeconds,
    [property: JsonPropertyName("page_count")] int? PageCount,
    [property: JsonPropertyName("device_make")] string? DeviceMake,
    [property: JsonPropertyName("device_model")] string? DeviceModel,
    [property: JsonPropertyName("latitude")] double? Latitude,
    [property: JsonPropertyName("longitude")] double? Longitude,
    [property: JsonPropertyName("location_name")] string? LocationName,
    [property: JsonPropertyName("favorite")] bool Favorite,
    [property: JsonPropertyName("hidden")] bool Hidden,
    [property: JsonPropertyName("archived_at")] DateTimeOffset? ArchivedAt,
    [property: JsonPropertyName("trashed_at")] DateTimeOffset? TrashedAt,
    [property: JsonPropertyName("source_count")] int SourceCount,
    [property: JsonPropertyName("files")] IReadOnlyList<LocalAssetFileDto> Files,
    [property: JsonPropertyName("tags")] IReadOnlyList<string> Tags,
    [property: JsonPropertyName("thumbnail_url")] string ThumbnailUrl,
    [property: JsonPropertyName("content_url")] string ContentUrl);

public sealed record LocalAssetFileDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("derivative_kind")] string? DerivativeKind,
    [property: JsonPropertyName("mime_type")] string MimeType,
    [property: JsonPropertyName("byte_size")] long ByteSize,
    [property: JsonPropertyName("source_count")] int SourceCount);

public sealed record LocalAssetPageDto(
    [property: JsonPropertyName("items")] IReadOnlyList<LocalAssetDto> Items,
    [property: JsonPropertyName("offset")] int Offset,
    [property: JsonPropertyName("limit")] int Limit,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("has_more")] bool HasMore);

public sealed record ViewLibrarySummaryDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("presentation")] string Presentation,
    [property: JsonPropertyName("visibility")] string Visibility,
    [property: JsonPropertyName("item_count")] int ItemCount,
    [property: JsonPropertyName("image_count")] int ImageCount,
    [property: JsonPropertyName("video_count")] int VideoCount,
    [property: JsonPropertyName("document_count")] int DocumentCount,
    [property: JsonPropertyName("audio_count")] int AudioCount);

public sealed record LocalAssetScanResultDto(
    [property: JsonPropertyName("library_id")] Guid LibraryId,
    [property: JsonPropertyName("files_seen")] int FilesSeen,
    [property: JsonPropertyName("items_added")] int ItemsAdded,
    [property: JsonPropertyName("files_added")] int FilesAdded,
    [property: JsonPropertyName("sources_added")] int SourcesAdded,
    [property: JsonPropertyName("duplicates_found")] int DuplicatesFound,
    [property: JsonPropertyName("errors")] int Errors);

public sealed record SetLocalAssetFlagRequest(
    [property: JsonPropertyName("value")] bool Value);
