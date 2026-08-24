namespace MediaEngine.Domain.PersonalMedia;

public enum ViewScopeKind
{
    Shared,
    Mine,
    Profile,
}

public enum ViewTimelineDensity
{
    Compact,
    Comfortable,
    Relaxed,
}

public sealed record ViewProfilePolicy(
    Guid ProfileId,
    bool ViewEnabled,
    bool AccessSharedView,
    bool IncludeInSharedView,
    bool ShareGalleries,
    DateTimeOffset? UpdatedAt)
{
    public static ViewProfilePolicy Default(Guid profileId) =>
        new(profileId, true, false, false, false, null);
}

public sealed record ViewProfilePreferences(
    Guid ProfileId,
    ViewScopeKind? LastScopeKind,
    Guid? LastScopeProfileId,
    ViewTimelineDensity TimelineDensity,
    DateTimeOffset? UpdatedAt)
{
    public static ViewProfilePreferences Default(Guid profileId) =>
        new(profileId, null, null, ViewTimelineDensity.Comfortable, null);
}

public sealed record ViewPersonalSpace(
    Guid Id,
    Guid OwnerProfileId,
    Guid LibraryId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public enum ViewSourceType
{
    Folder,
    BrowserUpload,
    DeviceImport,
    MobileBackup,
    Network,
    Other,
}

public sealed record ViewSource(
    Guid Id,
    Guid PersonalSpaceId,
    ViewSourceType SourceType,
    string Name,
    string? SourceKey,
    DateTimeOffset? LastActivityAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public enum ViewDeviceBackupState
{
    Unknown,
    Idle,
    BackingUp,
    Complete,
    Error,
}

public sealed record ViewDevice(
    Guid Id,
    Guid PersonalSpaceId,
    Guid? SourceId,
    string ClientDeviceId,
    string Name,
    string? Make,
    string? Model,
    DateTimeOffset? LastBackupAt,
    ViewDeviceBackupState BackupState,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public enum ViewGalleryKind
{
    Manual,
    Smart,
}

public enum ViewGallerySharePermission
{
    View,
    Contribute,
}

public sealed record ViewGallery(
    Guid Id,
    Guid OwnerProfileId,
    Guid PersonalSpaceId,
    string Name,
    string? Description,
    ViewGalleryKind Kind,
    string? SmartRuleJson,
    Guid? CoverItemId,
    int SortOrder,
    int ItemCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateViewGalleryCommand(
    Guid OwnerProfileId,
    Guid PersonalSpaceId,
    string Name,
    ViewGalleryKind Kind,
    string? Description = null,
    string? SmartRuleJson = null,
    Guid? CoverItemId = null,
    int SortOrder = 0);

public sealed record UpdateViewGalleryCommand(
    Guid GalleryId,
    string Name,
    string? Description,
    ViewGalleryKind Kind,
    string? SmartRuleJson,
    Guid? CoverItemId,
    int SortOrder);

public sealed record ViewGalleryItem(
    Guid GalleryId,
    Guid ItemId,
    int Position,
    DateTimeOffset AddedAt);

public sealed record ViewGalleryItemPage(
    IReadOnlyList<ViewGalleryItem> Items,
    int? NextPosition,
    Guid? NextItemId,
    bool HasMore);

public sealed record AddViewGalleryItemsResult(int Added, int AlreadyPresent);

public sealed record ViewGalleryShare(
    Guid GalleryId,
    Guid ProfileId,
    ViewGallerySharePermission Permission,
    DateTimeOffset SharedAt);
