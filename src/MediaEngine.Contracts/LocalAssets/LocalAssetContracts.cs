using System.Text.Json.Serialization;
using MediaEngine.Domain.PersonalMedia;

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

public sealed record ViewAssetTimelinePageDto(
    [property: JsonPropertyName("items")] IReadOnlyList<LocalAssetDto> Items,
    [property: JsonPropertyName("next_cursor")] string? NextCursor,
    [property: JsonPropertyName("has_more")] bool HasMore);

public sealed record ViewResolvedScopeDto(
    [property: JsonPropertyName("kind")] ViewScopeKind Kind,
    [property: JsonPropertyName("profile_id")] Guid? ProfileId,
    [property: JsonPropertyName("was_fallback")] bool WasFallback);

public sealed record ViewScopeOptionDto(
    [property: JsonPropertyName("kind")] ViewScopeKind Kind,
    [property: JsonPropertyName("profile_id")] Guid? ProfileId,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("avatar_color")] string? AvatarColor,
    [property: JsonPropertyName("avatar_url")] string? AvatarUrl);

public sealed record ViewScopeResolutionDto(
    [property: JsonPropertyName("scope")] ViewResolvedScopeDto Scope,
    [property: JsonPropertyName("available_scopes")] IReadOnlyList<ViewScopeOptionDto> AvailableScopes);

public sealed record ViewPreferencesDto(
    [property: JsonPropertyName("profile_id")] Guid ProfileId,
    [property: JsonPropertyName("scope")] ViewScopeKind? Scope,
    [property: JsonPropertyName("scope_profile_id")] Guid? ScopeProfileId,
    [property: JsonPropertyName("timeline_density")] ViewTimelineDensity TimelineDensity,
    [property: JsonPropertyName("updated_at")] DateTimeOffset? UpdatedAt);

public sealed record ViewUploadResponseDto(
    [property: JsonPropertyName("item_id")] Guid ItemId,
    [property: JsonPropertyName("item_added")] bool ItemAdded,
    [property: JsonPropertyName("files_added")] int FilesAdded,
    [property: JsonPropertyName("sources_added")] int SourcesAdded);

public sealed record ViewPreferencesRequest(
    [property: JsonPropertyName("scope")] string? Scope,
    [property: JsonPropertyName("scope_profile_id")] Guid? ScopeProfileId,
    [property: JsonPropertyName("timeline_density")] ViewTimelineDensity TimelineDensity);

public sealed record ViewGalleryRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("kind")] ViewGalleryKind Kind,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("smart_rule_json")] string? SmartRuleJson = null,
    [property: JsonPropertyName("cover_item_id")] Guid? CoverItemId = null,
    [property: JsonPropertyName("sort_order")] int SortOrder = 0);

public sealed record ViewGalleryListResponse(
    [property: JsonPropertyName("owned")] IReadOnlyList<ViewGalleryDto> Owned,
    [property: JsonPropertyName("shared_with_you")] IReadOnlyList<ViewGalleryDto> SharedWithYou);

public sealed record ViewGalleryDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("owner_profile_id")] Guid OwnerProfileId,
    [property: JsonPropertyName("personal_space_id")] Guid PersonalSpaceId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("kind")] ViewGalleryKind Kind,
    [property: JsonPropertyName("smart_rule_json")] string? SmartRuleJson,
    [property: JsonPropertyName("cover_item_id")] Guid? CoverItemId,
    [property: JsonPropertyName("sort_order")] int SortOrder,
    [property: JsonPropertyName("item_count")] int ItemCount,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

public sealed record ViewGalleryItemDto(
    [property: JsonPropertyName("gallery_id")] Guid GalleryId,
    [property: JsonPropertyName("item_id")] Guid ItemId,
    [property: JsonPropertyName("position")] int Position,
    [property: JsonPropertyName("added_at")] DateTimeOffset AddedAt);

public sealed record ViewGalleryItemPageDto(
    [property: JsonPropertyName("items")] IReadOnlyList<ViewGalleryItemDto> Items,
    [property: JsonPropertyName("next_position")] int? NextPosition,
    [property: JsonPropertyName("next_item_id")] Guid? NextItemId,
    [property: JsonPropertyName("has_more")] bool HasMore);

public sealed record AddViewGalleryItemsResponseDto(
    [property: JsonPropertyName("added")] int Added,
    [property: JsonPropertyName("already_present")] int AlreadyPresent);

public sealed record ViewGalleryShareDto(
    [property: JsonPropertyName("gallery_id")] Guid GalleryId,
    [property: JsonPropertyName("profile_id")] Guid ProfileId,
    [property: JsonPropertyName("permission")] ViewGallerySharePermission Permission,
    [property: JsonPropertyName("shared_at")] DateTimeOffset SharedAt);

public sealed record ViewGalleryItemsRequest(
    [property: JsonPropertyName("item_ids")] IReadOnlyCollection<Guid> ItemIds);

public sealed record ViewGalleryPositionRequest(
    [property: JsonPropertyName("position")] int Position);

public sealed record ViewGalleryShareRequest(
    [property: JsonPropertyName("profile_id")] Guid ProfileId,
    [property: JsonPropertyName("permission")] ViewGallerySharePermission Permission);

public sealed record ViewGallerySharesRequest(
    [property: JsonPropertyName("shares")] IReadOnlyCollection<ViewGalleryShareRequest> Shares);

public sealed record ViewItemsRemovedResponse(
    [property: JsonPropertyName("removed")] int Removed);

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
