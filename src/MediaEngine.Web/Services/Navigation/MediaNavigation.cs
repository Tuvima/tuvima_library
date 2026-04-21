using MediaEngine.Web.Models.ViewDTOs;

namespace MediaEngine.Web.Services.Navigation;

public static class MediaNavigation
{
    public static string ForWork(WorkViewModel work)
        => ForMedia(work.MediaType, work.Id, work.CollectionId);

    public static string ForJourney(JourneyItemViewModel item)
        => ForMedia(item.MediaType, item.WorkId, item.CollectionId);

    public static string ForSearchResult(SearchResultViewModel result)
        => ForMedia(result.MediaType, result.WorkId, result.CollectionId);

    public static string ForLibraryItem(LibraryItemViewModel item)
        => ForMedia(item.MediaType, item.EntityId, null);

    public static string ForCollection(CollectionViewModel collection)
    {
        var primaryMediaType = collection.PrimaryMediaType ?? collection.Works.FirstOrDefault()?.MediaType;
        var firstWorkId = collection.Works.FirstOrDefault()?.Id;
        return ForCollectionMedia(primaryMediaType, collection.Id, firstWorkId);
    }

    public static string ForContentGroup(ContentGroupViewModel group)
        => ForCollectionMedia(group.PrimaryMediaType, group.CollectionId);

    public static string ForCollectionMedia(string? mediaType, Guid collectionId, Guid? workId = null)
    {
        return NormalizeBucket(mediaType) switch
        {
            MediaBucket.Television => $"/watch/tv/show/{collectionId}",
            MediaBucket.Music => $"/listen/music/albums/{collectionId}",
            MediaBucket.Movie when workId.HasValue => $"/watch/movie/{workId.Value}?collectionId={collectionId}",
            MediaBucket.Read when workId.HasValue => ForMedia(mediaType, workId.Value, collectionId),
            _ => $"/collection/{collectionId}",
        };
    }

    public static string ForMedia(string? mediaType, Guid workId, Guid? collectionId = null)
    {
        return NormalizeBucket(mediaType) switch
        {
            MediaBucket.Television when collectionId.HasValue => $"/watch/tv/show/{collectionId.Value}/episode/{workId}",
            MediaBucket.Television => $"/book/{workId}",
            MediaBucket.Movie => collectionId.HasValue
                ? $"/watch/movie/{workId}?collectionId={collectionId.Value}"
                : $"/watch/movie/{workId}",
            MediaBucket.Music => collectionId.HasValue
                ? $"/listen/music/albums/{collectionId.Value}?track={workId}"
                : $"/listen/music/songs?track={workId}",
            MediaBucket.Audiobook => $"/book/{workId}?mode=listen",
            MediaBucket.Read => $"/book/{workId}?mode=read",
            _ => $"/book/{workId}",
        };
    }

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
