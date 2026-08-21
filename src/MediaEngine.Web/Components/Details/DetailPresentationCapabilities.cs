using MediaEngine.Contracts.Details;

namespace MediaEngine.Web.Components.Details;

public readonly record struct DetailPresentationCapabilities(
    bool IsMusicAlbum,
    bool IsAudiobook,
    bool IsBookish,
    bool IsWatchMedia,
    bool IsLongFormConsumable,
    bool SupportsCanonicalMissingItems,
    string StageModifier)
{
    public static DetailPresentationCapabilities For(DetailEntityType entityType)
    {
        var isMusicAlbum = entityType == DetailEntityType.MusicAlbum;
        var isAudiobook = entityType == DetailEntityType.Audiobook;
        var isBookish = entityType is DetailEntityType.Book
            or DetailEntityType.Audiobook
            or DetailEntityType.ComicIssue
            or DetailEntityType.Work;
        var isWatchMedia = entityType is DetailEntityType.Movie
            or DetailEntityType.TvShow
            or DetailEntityType.TvSeason
            or DetailEntityType.TvEpisode;

        return new DetailPresentationCapabilities(
            isMusicAlbum,
            isAudiobook,
            isBookish,
            isWatchMedia,
            isBookish || isWatchMedia,
            isMusicAlbum,
            entityType switch
            {
                DetailEntityType.MusicAlbum => "tl-detail-stage--music-album",
                DetailEntityType.Audiobook => "tl-detail-stage--audiobook",
                DetailEntityType.Book or DetailEntityType.ComicIssue or DetailEntityType.Work => "tl-detail-stage--bookish",
                DetailEntityType.Movie or DetailEntityType.TvShow or DetailEntityType.TvSeason or DetailEntityType.TvEpisode => "tl-detail-stage--watch",
                _ => string.Empty,
            });
    }
}
