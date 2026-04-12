namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// A single search result as rendered by the Command Palette autocomplete.
/// Maps directly from the Engine's <c>SearchResultDto</c>.
/// </summary>
public sealed class SearchResultViewModel
{
    public Guid    WorkId         { get; init; }
    public Guid?   CollectionId          { get; init; }
    public string  Title          { get; init; } = string.Empty;
    public string? OriginalTitle  { get; init; }
    public string? Author         { get; init; }
    public string  MediaType      { get; init; } = string.Empty;
    public string  CollectionDisplayName { get; init; } = string.Empty;
}
