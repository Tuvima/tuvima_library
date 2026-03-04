namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// A single search result as rendered by the Command Palette autocomplete.
/// Maps directly from the Engine's <c>SearchResultDto</c>.
/// </summary>
public sealed class SearchResultViewModel
{
    public Guid    WorkId         { get; init; }
    public Guid    HubId          { get; init; }
    public string  Title          { get; init; } = string.Empty;
    public string? Author         { get; init; }
    public string  MediaType      { get; init; } = string.Empty;
    public string  HubDisplayName { get; init; } = string.Empty;
}
