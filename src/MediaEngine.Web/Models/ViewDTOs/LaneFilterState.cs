namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Immutable filter state for any lane page.
/// Used by LaneFilterBar and MobileFilterBar to communicate active filters to MediaLanePage.
/// </summary>
public sealed record LaneFilterState
{
    public string Format { get; init; } = "All";
    public HashSet<string> SelectedGenres { get; init; } = [];
    public string SortBy { get; init; } = "recent";
    public string? PersonSearch { get; init; }
    public string? SeriesFilter { get; init; }
    public string? YearFrom { get; init; }
    public string? YearTo { get; init; }

    public bool HasActiveFilters =>
        Format != "All"
        || SelectedGenres.Count > 0
        || !string.IsNullOrEmpty(PersonSearch)
        || !string.IsNullOrEmpty(SeriesFilter)
        || !string.IsNullOrEmpty(YearFrom)
        || !string.IsNullOrEmpty(YearTo)
        || SortBy != "recent";
}
