namespace MediaEngine.Web.Models.ViewDTOs;

public sealed class DiscoveryHeroViewModel
{
    public string Eyebrow { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Subtitle { get; init; }
    public string? Description { get; init; }
    public string? Tldr { get; init; }
    public IReadOnlyList<string> VibeTags { get; init; } = [];
    public string? BackgroundImageUrl { get; init; }
    public string? HeroBackgroundImageUrl { get; init; }
    public string? BannerImageUrl { get; init; }
    public string? PreviewImageUrl { get; init; }
    public MediaTileSurfaceKind PreviewSurfaceKind { get; init; } = MediaTileSurfaceKind.CoverPortrait;
    public MediaTileImageFitMode TileImageFitMode { get; init; } = MediaTileImageFitMode.Fill;
    public MediaTileImageFitMode HoverImageFitMode { get; init; } = MediaTileImageFitMode.Contain;
    public string? LogoUrl { get; init; }
    public string AccentColor { get; init; } = "var(--tl-accent-primary)";
    public string? StatusText { get; init; }
    public string? MetaText { get; init; }
    public IReadOnlyList<string> MetaPills { get; init; } = [];
    public double? ProgressPct { get; init; }
    public Guid? RepresentativeEntityId { get; init; }
    public MediaTileSurfaceKind SurfaceKind { get; init; } = MediaTileSurfaceKind.BannerLandscape;
    public string PrimaryActionLabel { get; init; } = "Open";
    public string PrimaryNavigationUrl { get; init; } = "/";
    public string SecondaryActionLabel { get; init; } = "Details";
    public string? SecondaryNavigationUrl { get; init; }
}

public sealed class DiscoveryHubViewModel
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Subtitle { get; init; }
    public string? Description { get; init; }
    public string? ImageUrl { get; init; }
    public IReadOnlyList<string> PreviewImages { get; init; } = [];
    public string AccentColor { get; init; } = "var(--tl-accent-primary)";
    public string? Badge { get; init; }
    public string? CountLabel { get; init; }
    public string NavigationUrl { get; init; } = "/";
}

public sealed class DiscoveryPageViewModel
{
    public string Key { get; init; } = string.Empty;
    public string AccentColor { get; init; } = "var(--tl-accent-primary)";
    public DiscoveryHeroViewModel? Hero { get; init; }
    public IReadOnlyList<DiscoveryHeroViewModel> Spotlights { get; init; } = [];
    public IReadOnlyList<DiscoveryHubViewModel> Hubs { get; init; } = [];
    public IReadOnlyList<MediaTileShelfViewModel> Shelves { get; init; } = [];
    public IReadOnlyList<MediaTileViewModel> Catalog { get; init; } = [];
    public string EmptyTitle { get; init; } = "Your library is empty.";
    public string EmptySubtitle { get; init; } = "Add media to start discovering it here.";
}
