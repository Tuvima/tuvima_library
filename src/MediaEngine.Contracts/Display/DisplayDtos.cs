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
    IReadOnlyList<DisplayActionDto> Actions);

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
    DateTimeOffset SortTimestamp);

public sealed record DisplayArtworkDto(
    string? CoverUrl,
    string? SquareUrl,
    string? BannerUrl,
    string? BackgroundUrl,
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
