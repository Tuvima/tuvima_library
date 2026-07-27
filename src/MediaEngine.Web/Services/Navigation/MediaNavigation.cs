using MediaEngine.Web.Models.ViewDTOs;

namespace MediaEngine.Web.Services.Navigation;

public static class MediaNavigation
{
    public static string ForWork(WorkViewModel work, string? tab = null)
        => ForMedia(work.MediaType, work.Id, work.CollectionId, tab);

    public static string ForJourney(JourneyItemViewModel item, string? tab = null)
        => ForMedia(item.MediaType, item.WorkId, item.CollectionId, tab);

    public static string ForSearchResult(SearchResultDto result, string? tab = null)
        => ForMedia(result.MediaType, result.WorkId, result.CollectionId, tab);

    public static string ForLibraryItem(LibraryItemViewModel item, string? tab = null)
        => ForMedia(item.MediaType, item.EntityId, null, tab);

    public static string ForCollection(CollectionViewModel collection)
        => $"/details/collection/{collection.Id}?context={CollectionContext(collection.PrimaryMediaType ?? collection.Works.FirstOrDefault()?.MediaType)}";

    public static string ForContentGroup(ContentGroupViewModel group, string? tab = null)
        => NormalizeBucket(group.PrimaryMediaType) switch
        {
            MediaBucket.Television => $"/details/tvshow/{group.RootWorkId ?? group.CollectionId}?context=watch",
            MediaBucket.Music when group.RootWorkId.HasValue => $"/details/musicalbum/{group.RootWorkId.Value}?context=listen",
            _ => ForCollectionMedia(group.PrimaryMediaType, group.CollectionId, tab: tab),
        };

    public static string ForCollectionMedia(string? mediaType, Guid collectionId, Guid? workId = null, string? tab = null)
    {
        return NormalizeBucket(mediaType) switch
        {
            MediaBucket.Television => $"/details/tvshow/{workId ?? collectionId}?context=watch",
            MediaBucket.Music => $"/details/musicalbum/{collectionId}?context=listen",
            _ => $"/details/collection/{collectionId}?context={CollectionContext(mediaType)}",
        };
    }

    public static string ForMedia(string? mediaType, Guid workId, Guid? collectionId = null, string? tab = null)
    {
        return NormalizeBucket(mediaType) switch
        {
            MediaBucket.Television or MediaBucket.Movie => $"/details/work/{workId}?context=watch",
            MediaBucket.Music => $"/listen/music?browse=songs&track={workId}",
            MediaBucket.Audiobook => $"/details/work/{workId}?context=listen",
            MediaBucket.Read => $"/details/work/{workId}?context=read",
            _ => $"/details/work/{workId}",
        };
    }

    private static string CollectionContext(string? mediaType) =>
        NormalizeBucket(mediaType) switch
        {
            MediaBucket.Television or MediaBucket.Movie => "watch",
            MediaBucket.Music or MediaBucket.Audiobook => "listen",
            MediaBucket.Read => "read",
            _ => "default",
        };

    private static MediaBucket NormalizeBucket(string? mediaType)
    {
        var normalized = (mediaType ?? string.Empty).Trim().ToLowerInvariant();

        if (normalized.Contains("tv"))
        {
            return MediaBucket.Television;
        }

        if (normalized.Contains("movie") || normalized.Contains("video"))
        {
            return MediaBucket.Movie;
        }

        if (normalized.Contains("music") || normalized == "audio")
        {
            return MediaBucket.Music;
        }

        if (normalized.Contains("audiobook") || normalized.Contains("m4b"))
        {
            return MediaBucket.Audiobook;
        }

        if (normalized.Contains("book") || normalized.Contains("comic") || normalized.Contains("epub") || normalized.Contains("pdf"))
        {
            return MediaBucket.Read;
        }

        return MediaBucket.Unknown;
    }

    private enum MediaBucket
    {
        Unknown,
        Read,
        Audiobook,
        Music,
        Movie,
        Television,
    }
}
