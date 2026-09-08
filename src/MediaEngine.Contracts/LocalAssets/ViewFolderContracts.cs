using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.LocalAssets;

public sealed record ViewFolderSourceDto(
    [property: JsonPropertyName("source_id")] Guid SourceId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("owner_profile_id")] Guid OwnerProfileId,
    [property: JsonPropertyName("owner_name")] string OwnerName,
    [property: JsonPropertyName("storage_mode")] string StorageMode,
    [property: JsonPropertyName("include_in_timeline")] bool IncludeInTimeline,
    [property: JsonPropertyName("item_count")] int ItemCount,
    [property: JsonPropertyName("available")] bool Available);

public sealed record ViewFolderNodeDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("relative_path")] string RelativePath,
    [property: JsonPropertyName("item_count")] int ItemCount,
    [property: JsonPropertyName("pinned")] bool Pinned,
    [property: JsonPropertyName("include_in_timeline_override")] bool? IncludeInTimelineOverride);

public sealed record ViewFolderPinDto(
    [property: JsonPropertyName("source_id")] Guid SourceId,
    [property: JsonPropertyName("source_name")] string SourceName,
    [property: JsonPropertyName("relative_path")] string RelativePath,
    [property: JsonPropertyName("label")] string Label);

public sealed record ViewFolderBreadcrumbDto(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("relative_path")] string RelativePath);

public sealed record ViewFolderPageDto(
    [property: JsonPropertyName("sources")] IReadOnlyList<ViewFolderSourceDto> Sources,
    [property: JsonPropertyName("pinned_folders")] IReadOnlyList<ViewFolderPinDto> PinnedFolders,
    [property: JsonPropertyName("source_id")] Guid? SourceId,
    [property: JsonPropertyName("source_name")] string? SourceName,
    [property: JsonPropertyName("relative_path")] string RelativePath,
    [property: JsonPropertyName("pinned")] bool Pinned,
    [property: JsonPropertyName("effective_include_in_timeline")] bool EffectiveIncludeInTimeline,
    [property: JsonPropertyName("include_in_timeline_override")] bool? IncludeInTimelineOverride,
    [property: JsonPropertyName("breadcrumbs")] IReadOnlyList<ViewFolderBreadcrumbDto> Breadcrumbs,
    [property: JsonPropertyName("folders")] IReadOnlyList<ViewFolderNodeDto> Folders,
    [property: JsonPropertyName("items")] IReadOnlyList<LocalAssetDto> Items,
    [property: JsonPropertyName("offset")] int Offset,
    [property: JsonPropertyName("has_more")] bool HasMore);

public sealed record ViewFolderPinRequest(
    [property: JsonPropertyName("source_id")] Guid SourceId,
    [property: JsonPropertyName("relative_path")] string? RelativePath,
    [property: JsonPropertyName("pinned")] bool Pinned,
    [property: JsonPropertyName("scope")] string Scope = "mine",
    [property: JsonPropertyName("scope_profile_id")] Guid? ScopeProfileId = null);

public sealed record ViewFolderTimelinePolicyRequest(
    [property: JsonPropertyName("source_id")] Guid SourceId,
    [property: JsonPropertyName("relative_path")] string? RelativePath,
    [property: JsonPropertyName("include_in_timeline")] bool? IncludeInTimeline,
    [property: JsonPropertyName("scope")] string Scope = "mine",
    [property: JsonPropertyName("scope_profile_id")] Guid? ScopeProfileId = null);

public sealed record ViewFamilyTransferPreviewDto(
    [property: JsonPropertyName("item_id")] Guid ItemId,
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("file_count")] int FileCount,
    [property: JsonPropertyName("total_bytes")] long TotalBytes,
    [property: JsonPropertyName("destination_root")] string DestinationRoot,
    [property: JsonPropertyName("destination_kind")] string DestinationKind,
    [property: JsonPropertyName("source_cleanup_required")] bool SourceCleanupRequired,
    [property: JsonPropertyName("already_promoted")] bool AlreadyPromoted);

public sealed record ViewFamilyTransferRequest(
    [property: JsonPropertyName("destination_kind")] string DestinationKind = "timeline",
    [property: JsonPropertyName("folder_name")] string? FolderName = null);

public sealed record ViewFamilyTransferResultDto(
    [property: JsonPropertyName("item_id")] Guid ItemId,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("files_transferred")] int FilesTransferred,
    [property: JsonPropertyName("destination_paths")] IReadOnlyList<string> DestinationPaths,
    [property: JsonPropertyName("source_cleanup_pending")] bool SourceCleanupPending);
