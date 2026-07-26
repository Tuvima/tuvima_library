namespace MediaEngine.Contracts.Progress;

/// <summary>
/// An in-progress owned item with the work, collection, artwork, and resume context needed by
/// Dashboard journey surfaces.
/// </summary>
public sealed record JourneyItemDto(
    Guid AssetId,
    Guid WorkId,
    Guid? CollectionId,
    string Title,
    string? Author,
    string? CoverUrl,
    string? BackgroundUrl,
    string? BannerUrl,
    string? LogoUrl,
    int? CoverWidthPx,
    int? CoverHeightPx,
    int? BackgroundWidthPx,
    int? BackgroundHeightPx,
    int? BannerWidthPx,
    int? BannerHeightPx,
    string? Narrator,
    string? Series,
    string? SeriesPosition,
    string? Description,
    string MediaType,
    double ProgressPct,
    DateTimeOffset LastAccessed,
    string? CollectionDisplayName,
    Dictionary<string, string> ExtendedProperties,
    string? HeroUrl);
