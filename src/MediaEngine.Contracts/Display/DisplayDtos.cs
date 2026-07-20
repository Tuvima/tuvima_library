namespace MediaEngine.Contracts.Display;

public sealed record DisplayPageDto(
    string Key,
    string Title,
    string? Subtitle,
    DisplayHeroDto? Hero,
    IReadOnlyList<DisplayShelfDto> Shelves,
    IReadOnlyList<DisplayCardDto> Catalog);

public sealed record DisplayHeroDto(
    string Title,
    string? Subtitle,
    string? Eyebrow,
    DisplayArtworkDto Artwork,
    DisplayProgressDto? Progress,
    IReadOnlyList<DisplayActionDto> Actions)
{
    public IReadOnlyList<string> Facts { get; init; } = [];
    public Guid? Id { get; init; }
    public Guid? WorkId { get; init; }
    public Guid? CollectionId { get; init; }
    public string? MediaType { get; init; }
    public string? Presentation { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = [];
    public IReadOnlyList<DisplayCardPreviewItemDto> PreviewItems { get; init; } = [];
    public int? PreviewTotalCount { get; init; }
}

public sealed record DisplayShelfDto(
    string Key,
    string Title,
    string? Subtitle,
    IReadOnlyList<DisplayCardDto> Items,
    string? SeeAllRoute);

public sealed record DisplayShelfPageDto(
    DisplayShelfDto Shelf,
    string? NextCursor,
    int Offset,
    int Limit,
    bool HasMore);

public sealed record DisplayCardDto(
    Guid Id,
    Guid? WorkId,
    Guid? AssetId,
    Guid? CollectionId,
    string MediaType,
    string GroupingType,
    string Title,
    string? Subtitle,
    IReadOnlyList<string> Facts,
    DisplayArtworkDto Artwork,
    string PreferredShape,
    string Presentation,
    string TileTextMode,
    string PreviewPlacement,
    DisplayProgressDto? Progress,
    IReadOnlyList<DisplayActionDto> Actions,
    DisplayCardFlagsDto Flags,
    DateTimeOffset SortTimestamp)
{
    public string? Description { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = [];
    public IReadOnlyList<DisplayCardBadgeDto> Badges { get; init; } = [];
    public IReadOnlyList<DisplayCardPreviewItemDto> PreviewItems { get; init; } = [];
    public int? PreviewTotalCount { get; init; }
    public DisplayGroupSummaryDto? GroupSummary { get; init; }
}

public sealed record DisplayCardBadgeDto(
    string Kind,
    string Label);

public sealed record DisplayCardPreviewItemDto(
    Guid? WorkId,
    Guid? AssetId,
    string Title,
    string ImageUrl,
    string Shape,
    string? Position,
    string? MediaType = null,
    string? WebUrl = null,
    string? Description = null,
    IReadOnlyList<string>? Facts = null);

public sealed record DisplayGroupSummaryDto
{
    public int OwnedCount { get; init; }
    public int? KnownTotalCount { get; init; }
    public int CompletedCount { get; init; }
    public int InProgressCount { get; init; }
    public string? SequenceRange { get; init; }
    public string? RelationshipLabel { get; init; }
    public IReadOnlyList<DisplayGroupMediaCountDto> MediaCounts { get; init; } = [];
}

public sealed record DisplayGroupMediaCountDto(
    string MediaType,
    int Count);

public sealed record DisplayArtworkDto(
    string? CoverUrl,
    string? CoverSmallUrl,
    string? CoverMediumUrl,
    string? CoverLargeUrl,
    string? SquareUrl,
    string? SquareSmallUrl,
    string? SquareMediumUrl,
    string? SquareLargeUrl,
    string? BannerUrl,
    string? BannerSmallUrl,
    string? BannerMediumUrl,
    string? BannerLargeUrl,
    string? BackgroundUrl,
    string? BackgroundSmallUrl,
    string? BackgroundMediumUrl,
    string? BackgroundLargeUrl,
    string? LogoUrl,
    int? CoverWidthPx,
    int? CoverHeightPx,
    int? SquareWidthPx,
    int? SquareHeightPx,
    int? BannerWidthPx,
    int? BannerHeightPx,
    int? BackgroundWidthPx,
    int? BackgroundHeightPx,
    string? AccentColor);

public sealed record DisplayProgressDto(
    double Percent,
    string Label,
    DateTimeOffset? LastAccessed,
    DisplayActionDto? ResumeAction);

public sealed record DisplayActionDto(
    string Type,
    string Label,
    Guid? WorkId = null,
    Guid? AssetId = null,
    Guid? CollectionId = null,
    string? WebUrl = null);

public sealed record DisplayCardFlagsDto(
    bool IsPlayable,
    bool IsReadable,
    bool CanAddToCollection,
    bool IsCollection,
    bool IsFavorite);
