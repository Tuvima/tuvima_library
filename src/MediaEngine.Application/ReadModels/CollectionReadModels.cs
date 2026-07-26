namespace MediaEngine.Application.ReadModels;

public sealed record CollectionPaletteReadModel(
    string? PrimaryHex,
    string? SecondaryHex,
    string? AccentHex);

public sealed class CollectionArtistWorkReadModel
{
    public Guid WorkId { get; init; }
    public Guid? AssetId { get; init; }
    public string? Title { get; init; }
    public string? Album { get; init; }
    public string? Artist { get; init; }
    public string? TrackNumber { get; init; }
    public string? DiscNumber { get; init; }
    public string? AppleMusicId { get; init; }
    public string? ReleaseYear { get; init; }
    public string? YearValue { get; init; }
    public string? DurationSecondsValue { get; init; }
    public string? Duration { get; init; }
    public string? Runtime { get; init; }
    public string? Cover { get; init; }
    public string? Genre { get; init; }
    public string? ChildEntitiesJson { get; init; }
}

public sealed class CollectionSystemViewDetailWorkReadModel
{
    public Guid WorkId { get; init; }
    public Guid? AssetId { get; init; }
    public Guid? RootWorkId { get; init; }
    public string? Title { get; init; }
    public string? EpisodeTitle { get; init; }
    public string? ShowName { get; init; }
    public string? SeasonNumber { get; init; }
    public string? EpisodeNumber { get; init; }
    public string? Series { get; init; }
    public string? SeriesIndex { get; init; }
    public string? Album { get; init; }
    public string? Artist { get; init; }
    public string? Author { get; init; }
    public string? Director { get; init; }
    public string? TrackNumber { get; init; }
    public string? DiscNumber { get; init; }
    public string? AppleMusicId { get; init; }
    public string? ReleaseYear { get; init; }
    public string? YearValue { get; init; }
    public string? DurationSecondsValue { get; init; }
    public string? Duration { get; init; }
    public string? Runtime { get; init; }
    public string? Cover { get; init; }
    public string? Background { get; init; }
    public string? Banner { get; init; }
    public string? Hero { get; init; }
    public string? Logo { get; init; }
    public string? PrimaryColor { get; init; }
    public string? SecondaryColor { get; init; }
    public string? AccentColor { get; init; }
    public string? Genre { get; init; }
    public string? Network { get; init; }
    public string? ChildEntitiesJson { get; init; }
}
