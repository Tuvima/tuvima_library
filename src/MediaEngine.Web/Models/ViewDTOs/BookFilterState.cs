namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Immutable filter state for the Books lane page.
/// Used by BookFilterBar to communicate active filters to BooksLanePage.
/// </summary>
public sealed record BookFilterState
{
    public string Format { get; init; } = "All";
    public HashSet<string> SelectedGenres { get; init; } = [];
    public string SortBy { get; init; } = "recent";
    public string? AuthorSearch { get; init; }
    public string? SeriesFilter { get; init; }
    public string? YearFrom { get; init; }
    public string? YearTo { get; init; }

    public bool HasActiveFilters =>
        Format != "All"
        || SelectedGenres.Count > 0
        || !string.IsNullOrEmpty(AuthorSearch)
        || !string.IsNullOrEmpty(SeriesFilter)
        || !string.IsNullOrEmpty(YearFrom)
        || !string.IsNullOrEmpty(YearTo)
        || SortBy != "recent";
}