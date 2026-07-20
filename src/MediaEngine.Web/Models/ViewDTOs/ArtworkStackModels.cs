namespace MediaEngine.Web.Models.ViewDTOs;

public sealed class ArtworkStackItem
{
    public string Id { get; init; } = string.Empty;
    public Guid? WorkId { get; init; }
    public Guid? AssetId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string ImageUrl { get; init; } = string.Empty;
    public string MediaType { get; init; } = string.Empty;
    public string? NavigationUrl { get; init; }
    public ArtworkShape Shape { get; init; } = ArtworkShape.Portrait;
    public string? Position { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string> Facts { get; init; } = [];
}

public enum ArtworkShape
{
    Square,
    Portrait,
    Wide,
}

public enum ArtworkStackVariant
{
    Compact,
    Card,
    SeriesStrip,
    Hero,
    Dense,
}

public enum MediaArtworkGroupPreviewLayout
{
    Cluster,
    Strip,
    Adaptive,
}
