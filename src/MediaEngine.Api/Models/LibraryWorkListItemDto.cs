namespace MediaEngine.Api.Models;

public sealed class LibraryWorkListItemDto
{
    public Guid Id { get; init; }
    public Guid? CollectionId { get; init; }
    public Guid? RootWorkId { get; init; }
    public string MediaType { get; init; } = string.Empty;
    public string? WorkKind { get; init; }
    public int? Ordinal { get; init; }
    public string? WikidataQid { get; init; }
    public Guid? AssetId { get; init; }
    public string? CreatedAt { get; init; }
    public string? CoverUrl { get; init; }
    public string? BackgroundUrl { get; init; }
    public string? BannerUrl { get; init; }
    public string? HeroUrl { get; init; }
    public string? LogoUrl { get; init; }
    public Dictionary<string, string> CanonicalValues { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
