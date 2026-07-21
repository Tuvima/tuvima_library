namespace MediaEngine.Web.Models.ViewDTOs;

public sealed record MediaSectionNavigationItem(
    string Label,
    string Route,
    string Icon,
    string? Meta = null,
    bool Exact = false);

public sealed record MediaSectionNavigationGroup(
    string Label,
    IReadOnlyList<MediaSectionNavigationItem> Items);
