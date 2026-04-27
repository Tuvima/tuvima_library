using MediaEngine.Web.Models.ViewDTOs;

namespace MediaEngine.Web.Components.Browse;

public sealed record BrowseState(
    string ActiveTabId,
    string Grouping,
    string SearchText,
    string SortBy,
    LibraryLayoutMode Layout,
    Guid? GroupId,
    string? GroupType,
    string? GroupName,
    string? GroupField,
    string? GroupMediaType,
    string? ArtistName)
{
    public bool IsGroupDrilldown => GroupId.HasValue && !string.IsNullOrWhiteSpace(GroupType);
}
