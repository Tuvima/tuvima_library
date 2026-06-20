using MediaEngine.Domain.Models;

namespace MediaEngine.Web.Models.ViewDTOs;

public enum MediaTileShape
{
    Portrait,
    Landscape,
    Square,
}

public enum MediaTilePresentation
{
    Default,
    TvSeries,
    MovieSeries,
    BookSeries,
    ComicSeries,
    AudiobookSeries,
    Album,
    Artist,
}

public enum MediaTileSurfaceKind
{
    BannerLandscape,
    CoverPortrait,
    CoverSquare,
    ArtistPhotoSquare,
}

public enum MediaTileHoverLayout
{
    ArtOnlyPopover,
    BannerPopover,
}

public enum MediaTileImageFitMode
{
    Fill,
    Contain,
}

public enum MediaTileTextMode
{
    Caption,
    CoverOnly,
}

public enum MediaTilePreviewPlacement
{
    Smart,
    Bottom,
}

public enum MediaTileHoverMode
{
    None,
    Preview,
    Expanded,
}

public sealed record MediaTileMediaCountViewModel(string Icon, string Label, int Count);

public sealed class MediaTileViewModel
{
    public Guid Id { get; init; }
    public Guid? WorkId { get; init; }
    public Guid? CollectionId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Subtitle { get; init; }
    public string? Description { get; init; }
    public string? CoverUrl { get; init; }
    public string? BackgroundUrl { get; init; }
    public string? BannerUrl { get; init; }
    public string? HeroUrl { get; init; }
    public string? LogoUrl { get; init; }
    public IReadOnlyList<string> PreviewImages { get; init; } = [];
    public IReadOnlyList<ArtworkStackItem> ArtworkStackItems { get; init; } = [];
    public int? PreviewTotalCount { get; init; }
    public string? StatusText { get; init; }
    public string? MetaText { get; init; }
    public string? QualityBadge { get; init; }
    public string? SourceBadgeLabel { get; init; }
    public string? SourceLogoUrl { get; init; }
    public IReadOnlyList<MediaTileMediaCountViewModel> MediaCounts { get; init; } = [];
    public IReadOnlyList<string> ContextLines { get; init; } = [];
    public IReadOnlyList<string> HoverFacts { get; init; } = [];
    public string? Tldr { get; init; }
    public IReadOnlyList<string> VibeTags { get; init; } = [];
    public string MediaKind { get; init; } = string.Empty;
    public string AccentColor { get; init; } = "var(--tl-status-info)";
    public string SecondaryAccentColor { get; init; } = "#111827";
    public ArtworkPalette? ArtworkPalette { get; init; }
    public MediaTileShape Shape { get; init; } = MediaTileShape.Portrait;
    public MediaTilePresentation Presentation { get; init; } = MediaTilePresentation.Default;
    public MediaTileSurfaceKind SurfaceKind { get; init; } = MediaTileSurfaceKind.CoverPortrait;
    public MediaTileHoverLayout HoverLayout { get; init; } = MediaTileHoverLayout.ArtOnlyPopover;
    public MediaTileHoverMode HoverMode { get; init; } = MediaTileHoverMode.Expanded;
    public MediaTileTextMode TileTextMode { get; init; } = MediaTileTextMode.Caption;
    public MediaTilePreviewPlacement PreviewPlacement { get; init; } = MediaTilePreviewPlacement.Smart;
    public string? TileImageUrl { get; init; }
    public string? TileImageSrcSet { get; init; }
    public string TileImageSizes { get; init; } = "(max-width: 640px) 44vw, 220px";
    public string? HoverImageUrl { get; init; }
    public string? HoverImageSrcSet { get; init; }
    public string HoverImageSizes { get; init; } = "(max-width: 640px) 80vw, 560px";
    public string? HeroBackgroundImageUrl { get; init; }
    public string? PreviewImageUrl { get; init; }
    public MediaTileImageFitMode TileImageFitMode { get; init; } = MediaTileImageFitMode.Fill;
    public MediaTileImageFitMode HoverImageFitMode { get; init; } = MediaTileImageFitMode.Contain;
    public Guid? RepresentativeEntityId { get; init; }
    public string NavigationUrl { get; init; } = "/";
    public string? PrimaryNavigationUrl { get; init; }
    public string? DetailsNavigationUrl { get; init; }
    public string PrimaryActionLabel { get; init; } = "Open";
    public double? ProgressPct { get; init; }
    public string? ProgressLabel { get; init; }
    public string? Creator { get; init; }
    public string? CollectionKey { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = [];
    public int SortYear { get; init; }
    public DateTimeOffset SortTimestamp { get; init; }
    public bool IsCollection { get; init; }

    public bool CanAddToCollection => WorkId.HasValue && !IsCollection;
}

public sealed class MediaTileShelfViewModel
{
    public string Title { get; init; } = string.Empty;
    public string? Subtitle { get; init; }
    public IReadOnlyList<MediaTileViewModel> Items { get; init; } = [];
    public string? SeeAllRoute { get; init; }
}
