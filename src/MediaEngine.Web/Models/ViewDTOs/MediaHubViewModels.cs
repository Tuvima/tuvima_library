namespace MediaEngine.Web.Models.ViewDTOs;

public enum MediaHubSectionType
{
    Read,
    Watch,
    Listen,
}

public enum MediaArtworkShape
{
    Landscape,
    LandscapeWide,
    Square,
    Portrait,
    BookPortrait,
    Stack,
}

public sealed record MediaHubModeViewModel(
    string Id,
    string Label,
    string Route);

public sealed class MediaHubShelfViewModel
{
    public string Key { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Subtitle { get; init; }
    public IReadOnlyList<MediaTileViewModel> Items { get; init; } = [];
    public string? SeeAllRoute { get; init; }
    public MediaTileShape? ShapeOverride { get; init; }
    public bool IsContinueShelf { get; init; }
    public bool SupportsMixedArtwork { get; init; } = true;

    public MediaTileShelfViewModel ToMediaTileShelf() => new()
    {
        Title = Title,
        Subtitle = Subtitle,
        Items = Items,
        SeeAllRoute = SeeAllRoute,
    };

    public static MediaHubShelfViewModel FromShelf(
        string key,
        MediaTileShelfViewModel shelf,
        bool isContinueShelf = false,
        MediaTileShape? shapeOverride = null) => new()
        {
            Key = key,
            Title = shelf.Title,
            Subtitle = shelf.Subtitle,
            Items = shelf.Items,
            SeeAllRoute = shelf.SeeAllRoute,
            IsContinueShelf = isContinueShelf,
            ShapeOverride = shapeOverride,
        };
}
