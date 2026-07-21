using MediaEngine.Web.Models.ViewDTOs;

namespace MediaEngine.Web.Components.Browse;

public sealed record BrowseState(
    string ActiveTabId,
    string Grouping,
    string SearchText,
    string SortBy,
    LibraryLayoutMode Layout,
    IReadOnlyList<string> Genres,
    string Creator,
    string Status,
    string Year);
