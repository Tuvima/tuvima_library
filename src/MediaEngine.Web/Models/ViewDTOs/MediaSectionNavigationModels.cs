namespace MediaEngine.Web.Models.ViewDTOs;

public sealed record MediaSectionNavigationItem(
    string Label,
    string Route,
    string Icon,
    string? Meta = null,
    bool Exact = false,
    MediaSectionNavigationDropTarget? DropTarget = null,
    IReadOnlyList<MediaSectionNavigationItem>? Children = null);

public sealed record MediaSectionNavigationGroup(
    string Label,
    IReadOnlyList<MediaSectionNavigationItem> Items);

/// <summary>
/// Identifies the destination represented by a navigation rail drop target.
/// The concrete record type keeps feature handlers from confusing playlists,
/// Galleries, and other containers that may happen to share a Guid identifier.
/// </summary>
public abstract record MediaSectionNavigationDropTarget;

public sealed record PlaylistNavigationDropTarget(Guid PlaylistId) : MediaSectionNavigationDropTarget;

public sealed record ManualGalleryNavigationDropTarget(Guid GalleryId) : MediaSectionNavigationDropTarget;

public sealed record NewGalleryNavigationDropTarget : MediaSectionNavigationDropTarget;

public sealed record ContainerNavigationDropTarget(string ContainerKind, Guid? ContainerId = null)
    : MediaSectionNavigationDropTarget;

public sealed record MediaSectionNavigationDropEvent(
    MediaSectionNavigationItem Item,
    MediaSectionNavigationDropTarget Target);
