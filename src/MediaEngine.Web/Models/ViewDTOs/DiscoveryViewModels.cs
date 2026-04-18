namespace MediaEngine.Web.Models.ViewDTOs;

public enum DiscoveryCardShape
{
    Portrait,
    Landscape,
    Square,
}

public sealed class DiscoveryHeroViewModel
{
    public string Eyebrow { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Subtitle { get; init; }
    public string? Description { get; init; }
    public string? BackgroundImageUrl { get; init; }
    public string? PreviewImageUrl { get; init; }
    public string AccentColor { get; init; } = "#C9922E";
    public string? MetaText { get; init; }
    public double? ProgressPct { get; init; }
    public string PrimaryActionLabel { get; init; } = "Open";
    public string PrimaryNavigationUrl { get; init; } = "/";
    public string SecondaryActionLabel { get; init; } = "Details";
    public string? SecondaryNavigationUrl { get; init; }
}

public sealed class DiscoveryCardViewModel
{
    public Guid Id { get; init; }
    public Guid? WorkId { get; init; }
    public Guid? CollectionId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Subtitle { get; init; }
    public string? Description { get; init; }
    public string? CoverUrl { get; init; }
    public string? BackdropUrl { get; init; }
    public IReadOnlyList<string> PreviewImages { get; init; } = [];
    public string? MetaText { get; init; }
    public string MediaKind { get; init; } = string.Empty;
    public string AccentColor { get; init; } = "#60A5FA";
    public DiscoveryCardShape Shape { get; init; } = DiscoveryCardShape.Portrait;
    public string NavigationUrl { get; init; } = "/";
    public string? DetailsNavigationUrl { get; init; }
    public string PrimaryActionLabel { get; init; } = "Open";
    public double? ProgressPct { get; init; }
    public string? Creator { get; init; }
    public string? CollectionKey { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = [];
    public int SortYear { get; init; }
    public DateTimeOffset SortTimestamp { get; init; }
    public bool IsCollection { get; init; }

    public bool CanAddToCollection => WorkId.HasValue && !IsCollection;
}

public sealed class DiscoveryHubViewModel
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Subtitle { get; init; }
    public string? Description { get; init; }
    public string? ImageUrl { get; init; }
    public IReadOnlyList<string> PreviewImages { get; init; } = [];
    public string AccentColor { get; init; } = "#1CE783";
    public string? Badge { get; init; }
    public string? CountLabel { get; init; }
    public string NavigationUrl { get; init; } = "/";
}

public sealed class DiscoveryShelfViewModel
{
    public string Title { get; init; } = string.Empty;
    public string? Subtitle { get; init; }
    public IReadOnlyList<DiscoveryCardViewModel> Items { get; init; } = [];
    public string? SeeAllRoute { get; init; }
}

public sealed class DiscoveryPageViewModel
{
    public string Key { get; init; } = string.Empty;
    public string AccentColor { get; init; } = "#C9922E";
    public DiscoveryHeroViewModel? Hero { get; init; }
    public IReadOnlyList<DiscoveryHubViewModel> Hubs { get; init; } = [];
    public IReadOnlyList<DiscoveryShelfViewModel> Shelves { get; init; } = [];
    public IReadOnlyList<DiscoveryCardViewModel> Catalog { get; init; } = [];
    public string EmptyTitle { get; init; } = "Your library is empty.";
    public string EmptySubtitle { get; init; } = "Add media to start discovering it here.";
}
