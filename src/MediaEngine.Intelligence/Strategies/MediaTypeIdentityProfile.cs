using MediaEngine.Domain;
using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Intelligence.Strategies;

/// <summary>
/// Declarative identity rules for one media type.
/// </summary>
public sealed record MediaTypeIdentityProfile(
    MediaType MediaType,
    IReadOnlyList<string> PreferredBridgeIds,
    IReadOnlyList<string> CriticalFields,
    bool AllowsTextFallback,
    double TextFallbackMinConfidence,
    bool RequiresCreatorForFallback) : IMediaTypeIdentityStrategy;

/// <summary>
/// The complete set of supported media-type identity profiles.
/// </summary>
public static class MediaTypeIdentityProfileCatalog
{
    public static MediaTypeIdentityProfile Books { get; } = new(
        MediaType.Books,
        [BridgeIdKeys.Isbn, BridgeIdKeys.OpenLibraryId],
        [MetadataFieldConstants.Author, MetadataFieldConstants.Title],
        AllowsTextFallback: true,
        TextFallbackMinConfidence: ConfidenceBand.StrongFloor,
        RequiresCreatorForFallback: true);

    public static MediaTypeIdentityProfile Audiobooks { get; } = new(
        MediaType.Audiobooks,
        [BridgeIdKeys.Isbn, BridgeIdKeys.Asin, BridgeIdKeys.AppleMusicId],
        [MetadataFieldConstants.Author, MetadataFieldConstants.Narrator, MetadataFieldConstants.Title],
        AllowsTextFallback: true,
        TextFallbackMinConfidence: ConfidenceBand.StrongFloor,
        RequiresCreatorForFallback: true);

    public static MediaTypeIdentityProfile Movies { get; } = new(
        MediaType.Movies,
        [BridgeIdKeys.TmdbId],
        [MetadataFieldConstants.Title, MetadataFieldConstants.Year],
        AllowsTextFallback: true,
        TextFallbackMinConfidence: ConfidenceBand.StrongFloor,
        RequiresCreatorForFallback: false);

    public static MediaTypeIdentityProfile TV { get; } = new(
        MediaType.TV,
        [BridgeIdKeys.TmdbId, BridgeIdKeys.TvdbId],
        [
            MetadataFieldConstants.ShowName,
            MetadataFieldConstants.SeasonNumber,
            MetadataFieldConstants.EpisodeNumber,
        ],
        AllowsTextFallback: true,
        TextFallbackMinConfidence: ConfidenceBand.StrongFloor,
        RequiresCreatorForFallback: false);

    public static MediaTypeIdentityProfile Music { get; } = new(
        MediaType.Music,
        [
            BridgeIdKeys.AppleMusicId,
            BridgeIdKeys.AppleMusicCollectionId,
            BridgeIdKeys.MusicBrainzId,
        ],
        [
            MetadataFieldConstants.Artist,
            MetadataFieldConstants.Album,
            MetadataFieldConstants.Title,
        ],
        AllowsTextFallback: false,
        TextFallbackMinConfidence: ConfidenceBand.StrongFloor,
        RequiresCreatorForFallback: false);

    public static MediaTypeIdentityProfile Comics { get; } = new(
        MediaType.Comics,
        ["comicvine_id"],
        [
            MetadataFieldConstants.Series,
            MetadataFieldConstants.IssueNumber,
            MetadataFieldConstants.Title,
        ],
        AllowsTextFallback: true,
        TextFallbackMinConfidence: ConfidenceBand.StrongFloor,
        RequiresCreatorForFallback: false);

    public static IReadOnlyList<MediaTypeIdentityProfile> All { get; } =
        [Books, Audiobooks, Movies, TV, Music, Comics];

    public static MediaTypeIdentityProfile Get(MediaType mediaType) =>
        All.FirstOrDefault(profile => profile.MediaType == mediaType)
        ?? throw new ArgumentOutOfRangeException(
            nameof(mediaType),
            mediaType,
            "No identity profile is registered for the media type.");
}
