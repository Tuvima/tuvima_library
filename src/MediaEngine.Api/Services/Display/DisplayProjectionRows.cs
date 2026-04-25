namespace MediaEngine.Api.Services.Display;

public interface IDisplayArtworkRow
{
    string? CoverUrl { get; }
    string? SquareUrl { get; }
    string? BannerUrl { get; }
    string? BackgroundUrl { get; }
    string? LogoUrl { get; }
    string? CoverWidthPx { get; }
    string? CoverHeightPx { get; }
    string? SquareWidthPx { get; }
    string? SquareHeightPx { get; }
    string? BannerWidthPx { get; }
    string? BannerHeightPx { get; }
    string? BackgroundWidthPx { get; }
    string? BackgroundHeightPx { get; }
    string? AccentColor { get; }
}

public sealed class DisplayWorkRow : IDisplayArtworkRow
{
    public Guid WorkId { get; set; }
    public Guid? CollectionId { get; set; }
    public string MediaType { get; set; } = string.Empty;
    public string? WorkKind { get; set; }
    public Guid RootWorkId { get; set; }
    public Guid AssetId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Author { get; set; }
    public string? Artist { get; set; }
    public string? Album { get; set; }
    public string? Year { get; set; }
    public string? Genre { get; set; }
    public string? Series { get; set; }
    public string? SeriesPosition { get; set; }
    public string? Narrator { get; set; }
    public string? Director { get; set; }
    public string? Network { get; set; }
    public string? ShowName { get; set; }
    public string? SeasonNumber { get; set; }
    public string? EpisodeNumber { get; set; }
    public string? TrackNumber { get; set; }
    public string? CoverUrl { get; set; }
    public string? SquareUrl { get; set; }
    public string? BannerUrl { get; set; }
    public string? BackgroundUrl { get; set; }
    public string? LogoUrl { get; set; }
    public string? CoverState { get; set; }
    public string? SquareState { get; set; }
    public string? BannerState { get; set; }
    public string? BackgroundState { get; set; }
    public string? LogoState { get; set; }
    public string? CoverWidthPx { get; set; }
    public string? CoverHeightPx { get; set; }
    public string? SquareWidthPx { get; set; }
    public string? SquareHeightPx { get; set; }
    public string? BannerWidthPx { get; set; }
    public string? BannerHeightPx { get; set; }
    public string? BackgroundWidthPx { get; set; }
    public string? BackgroundHeightPx { get; set; }
    public string? AccentColor { get; set; }
}

public sealed class DisplayJourneyRow : IDisplayArtworkRow
{
    public Guid AssetId { get; set; }
    public Guid WorkId { get; set; }
    public Guid? CollectionId { get; set; }
    public string MediaType { get; set; } = string.Empty;
    public double ProgressPct { get; set; }
    public DateTimeOffset LastAccessed { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Author { get; set; }
    public string? Artist { get; set; }
    public string? Album { get; set; }
    public string? Year { get; set; }
    public string? Genre { get; set; }
    public string? Series { get; set; }
    public string? Narrator { get; set; }
    public string? SeasonNumber { get; set; }
    public string? EpisodeNumber { get; set; }
    public string? TrackNumber { get; set; }
    public string? CoverUrl { get; set; }
    public string? SquareUrl { get; set; }
    public string? BannerUrl { get; set; }
    public string? BackgroundUrl { get; set; }
    public string? LogoUrl { get; set; }
    public string? CoverState { get; set; }
    public string? SquareState { get; set; }
    public string? BannerState { get; set; }
    public string? BackgroundState { get; set; }
    public string? LogoState { get; set; }
    public string? CoverWidthPx { get; set; }
    public string? CoverHeightPx { get; set; }
    public string? SquareWidthPx { get; set; }
    public string? SquareHeightPx { get; set; }
    public string? BannerWidthPx { get; set; }
    public string? BannerHeightPx { get; set; }
    public string? BackgroundWidthPx { get; set; }
    public string? BackgroundHeightPx { get; set; }
    public string? AccentColor { get; set; }
}
