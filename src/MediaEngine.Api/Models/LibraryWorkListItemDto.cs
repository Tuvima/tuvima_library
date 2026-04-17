namespace MediaEngine.Api.Models;

public sealed class LibraryWorkListItemDto
{
    public Guid Id { get; init; }
    public string MediaType { get; init; } = string.Empty;
    public int? Ordinal { get; init; }
    public string? WikidataQid { get; init; }
    public Guid? AssetId { get; init; }
    public string? CreatedAt { get; init; }
    public Dictionary<string, string> CanonicalValues { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
