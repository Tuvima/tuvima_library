namespace MediaEngine.Web.Models.ViewDTOs;

public sealed record DisplayPageViewModel(
    string Key,
    string Title,
    string? Subtitle,
    DisplayHeroViewModel? Hero,
    IReadOnlyList<DisplayShelfViewModel> Shelves,
    IReadOnlyList<DisplayCardViewModel> Catalog);

public sealed record DisplayHeroViewModel(
    string Title,
    string? Subtitle,
    string? Eyebrow,
    DisplayArtworkViewModel Artwork,
    DisplayProgressViewModel? Progress,
    IReadOnlyList<DisplayActionViewModel> Actions);

public sealed record DisplayShelfViewModel(
    string Key,
    string Title,
    string? Subtitle,
    IReadOnlyList<DisplayCardViewModel> Items,
    string? SeeAllRoute);

public sealed record DisplayCardViewModel(
    Guid Id,
    Guid? WorkId,
    Guid? AssetId,
    Guid? CollectionId,
    string MediaType,
    string GroupingType,
    string Title,
    string? Subtitle,
    IReadOnlyList<string> Facts,
    DisplayArtworkViewModel Artwork,
    string PreferredShape,
    string Presentation,
    string TileTextMode,
    string PreviewPlacement,
    DisplayProgressViewModel? Progress,
    IReadOnlyList<DisplayActionViewModel> Actions,
    DisplayCardFlagsViewModel Flags,
    DateTimeOffset SortTimestamp);

public sealed record DisplayArtworkViewModel(
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

public sealed record DisplayProgressViewModel(
    double Percent,
    string Label,
    DateTimeOffset? LastAccessed,
    DisplayActionViewModel? ResumeAction);

public sealed record DisplayActionViewModel(
    string Type,
    string Label,
    Guid? WorkId = null,
    Guid? AssetId = null,
    Guid? CollectionId = null,
    string? WebUrl = null);

public sealed record DisplayCardFlagsViewModel(
    bool IsPlayable,
    bool IsReadable,
    bool CanAddToCollection,
    bool IsCollection,
    bool IsFavorite);
