using MediaEngine.Web.Models.ViewDTOs;

namespace MediaEngine.Web.Services.Navigation;

public static class MediaNavigation
{
    public static string ForWork(WorkViewModel work, string? tab = null)
        => ForMedia(work.MediaType, work.Id, work.CollectionId, tab);

    public static string ForJourney(JourneyItemViewModel item, string? tab = null)
        => ForMedia(item.MediaType, item.WorkId, item.CollectionId, tab);

    public static string ForSearchResult(SearchResultViewModel result, string? tab = null)
        => ForMedia(result.MediaType, result.WorkId, result.CollectionId, tab);

    public static string ForLibraryItem(LibraryItemViewModel item, string? tab = null)
        => ForMedia(item.MediaType, item.EntityId, null, tab);

    public static string ForCollection(CollectionViewModel collection)
    {
        var primaryMediaType = collection.PrimaryMediaType ?? collection.Works.FirstOrDefault()?.MediaType;
        var firstWorkId = collection.Works.FirstOrDefault()?.Id;
        return ForCollectionMedia(primaryMediaType, collection.Id, firstWorkId);
    }

    public static string ForContentGroup(ContentGroupViewModel group, string? tab = null)
        => NormalizeBucket(group.PrimaryMediaType) == MediaBucket.Television && group.RootWorkId.HasValue
            ? $"/watch/tv/show/{group.RootWorkId.Value}"
            : NormalizeBucket(group.PrimaryMediaType) == MediaBucket.Music && group.RootWorkId.HasValue
                ? $"/details/musicalbum/{group.RootWorkId.Value}?context=listen"
                : ForCollectionMedia(group.PrimaryMediaType, group.CollectionId, tab: tab);

    public static string ForCollectionMedia(string? mediaType, Guid collectionId, Guid? workId = null, string? tab = null)
    {
        return NormalizeBucket(mediaType) switch
        {
            MediaBucket.Television => workId.HasValue
                ? $"/watch/tv/show/{workId.Value}"
                : $"/watch/tv/show/{collectionId}",
            MediaBucket.Music => $"/details/musicalbum/{collectionId}?context=listen",
            MediaBucket.Movie when workId.HasValue => $"/details/movie/{workId.Value}?context=watch",
            MediaBucket.Read when workId.HasValue => ForMedia(mediaType, workId.Value, collectionId),
            _ => $"/details/collection/{collectionId}",
        };
    }

    public static string ForMedia(string? mediaType, Guid workId, Guid? collectionId = null, string? tab = null)
    {
        return NormalizeBucket(mediaType) switch
        {
            MediaBucket.Television when collectionId.HasValue => $"/watch/tv/show/{collectionId.Value}",
            MediaBucket.Television => "/watch",
            MediaBucket.Movie => $"/details/movie/{workId}?context=watch",
            MediaBucket.Music => $"/details/musictrack/{workId}?context=listen",
            MediaBucket.Audiobook => $"/details/audiobook/{workId}?context=listen",
            MediaBucket.Read => $"/details/{ResolveReadEntity(mediaType)}/{workId}?context=read",
            _ => $"/details/work/{workId}",
        };
    }

    private static string ResolveReadEntity(string? mediaType)
        => mediaType?.Contains("comic", StringComparison.OrdinalIgnoreCase) == true
            ? "comicissue"
            : "book";

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
