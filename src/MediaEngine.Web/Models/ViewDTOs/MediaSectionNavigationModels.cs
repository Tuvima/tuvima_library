namespace MediaEngine.Web.Models.ViewDTOs;

public sealed record MediaSectionNavigationItem(
    string Label,
    string Route,
    string Icon,
    string? Meta = null,
    bool Exact = false,
    Guid? DropCollectionId = null);

public sealed record MediaSectionNavigationGroup(
    string Label,
    IReadOnlyList<MediaSectionNavigationItem> Items);
