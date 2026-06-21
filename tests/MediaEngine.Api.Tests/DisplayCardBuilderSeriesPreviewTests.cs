using MediaEngine.Api.Services.Display;

namespace MediaEngine.Api.Tests;

public sealed class DisplayCardBuilderSeriesPreviewTests
{
    [Fact]
    public void BuildCollectionCards_UsesOrderedPreviewItemsForReadSeries()
    {
        var collectionId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var createdAt = DateTimeOffset.Parse("2026-06-01T12:00:00Z");
        var works = new[]
        {
            CreateWork(collectionId, "Book", "Book Five", "5", "/covers/5-s.jpg", createdAt.AddMinutes(5)),
            CreateWork(collectionId, "Book", "Book Two", "2", "/covers/2-s.jpg", createdAt.AddMinutes(2)),
            CreateWork(collectionId, "Book", "Book Four", "4", "/covers/4-s.jpg", createdAt.AddMinutes(4)),
            CreateWork(collectionId, "Book", "Book One", "1", "/covers/1-s.jpg", createdAt.AddMinutes(1)),
            CreateWork(collectionId, "Book", "Book Three", "3", "/covers/3-s.jpg", createdAt.AddMinutes(3)),
        };

        var card = new DisplayCardBuilder()
            .BuildCollectionCards(works, "read", minimumSeriesItems: 2)
            .Single();

        Assert.Equal("bookSeries", card.Presentation);
        Assert.Equal("5 owned titles", card.Subtitle);
        Assert.Equal(["5 owned titles"], card.Facts);
        Assert.Equal(5, card.PreviewTotalCount);
        Assert.Equal(["Book One", "Book Two", "Book Three", "Book Four"], card.PreviewItems.Select(item => item.Title));
        Assert.Equal(["1", "2", "3", "4"], card.PreviewItems.Select(item => item.Position));
        Assert.Equal(["/covers/1-s.jpg", "/covers/2-s.jpg", "/covers/3-s.jpg", "/covers/4-s.jpg"], card.PreviewItems.Select(item => item.ImageUrl));
    }

    [Fact]
    public void BuildCollectionCards_UsesManifestTotalForKnownSeriesSize()
    {
        var collectionId = Guid.Parse("44444444-2222-3333-4444-555555555555");
        var createdAt = DateTimeOffset.Parse("2026-06-01T12:00:00Z");
        var works = new[]
        {
            CreateWork(collectionId, "Comic", "The Sandman: Sleep of the Just", "1", "/covers/sandman-1.jpg", createdAt.AddMinutes(1), manifestTotalCount: 75),
            CreateWork(collectionId, "Comic", "The Sandman: Imperfect Hosts", "2", "/covers/sandman-2.jpg", createdAt.AddMinutes(2), manifestTotalCount: 75),
        };

        var card = new DisplayCardBuilder()
            .BuildCollectionCards(works, "read", minimumSeriesItems: 2)
            .Single();

        Assert.Equal("comicSeries", card.Presentation);
        Assert.Equal("2 of 75 issues owned", card.Subtitle);
        Assert.Equal(["2 of 75 issues owned"], card.Facts);
        Assert.Equal(75, card.PreviewTotalCount);
    }

    [Fact]
    public void BuildWatchGroupCards_DoesNotPromoteSingleMovieAsSeries()
    {
        var collectionId = Guid.Parse("22222222-2222-3333-4444-555555555555");
        var works = new[]
        {
            CreateWork(collectionId, "Movie", "The Matrix", "1", "/covers/matrix-s.jpg", DateTimeOffset.Parse("2026-06-01T12:00:00Z")),
        };

        var cards = new DisplayCardBuilder().BuildWatchGroupCards(works, minimumSeriesItems: 2);

        Assert.Empty(cards);
    }

    [Fact]
    public void BuildTvShowCards_UsesRootShowArtworkWithoutSeriesPreviewStack()
    {
        var rootWorkId = Guid.Parse("33333333-2222-3333-4444-555555555555");
        var works = new[]
        {
            CreateTvEpisode(rootWorkId, "Foundation", "1", "1", "/episodes/1.jpg", DateTimeOffset.Parse("2026-06-01T12:00:00Z")),
            CreateTvEpisode(rootWorkId, "Foundation", "1", "2", "/episodes/2.jpg", DateTimeOffset.Parse("2026-06-02T12:00:00Z")),
        };

        var card = new DisplayCardBuilder().BuildTvShowCards(works).Single();

        Assert.Equal("tvSeries", card.Presentation);
        Assert.Equal("Foundation", card.Title);
        Assert.Equal("/shows/foundation-bg-s.jpg", card.Artwork.BackgroundSmallUrl);
        Assert.Empty(card.PreviewItems);
        Assert.Null(card.PreviewTotalCount);
    }

    private static DisplayWorkRow CreateWork(
        Guid collectionId,
        string mediaType,
        string title,
        string position,
        string coverSmallUrl,
        DateTimeOffset createdAt,
        int manifestTotalCount = 0) =>
        new()
        {
            WorkId = Guid.NewGuid(),
            AssetId = Guid.NewGuid(),
            CollectionId = collectionId,
            MediaType = mediaType,
            CreatedAt = createdAt,
            Title = title,
            Series = "Foundation Series",
            SeriesPosition = position,
            CollectionTitle = "Foundation Series",
            CollectionManifestTotalCount = manifestTotalCount,
            CoverSmallUrl = coverSmallUrl,
            CoverWidthPx = "1000",
            CoverHeightPx = "1500",
        };

    private static DisplayWorkRow CreateTvEpisode(
        Guid rootWorkId,
        string showName,
        string season,
        string episode,
        string episodeBackgroundUrl,
        DateTimeOffset createdAt) =>
        new()
        {
            WorkId = Guid.NewGuid(),
            AssetId = Guid.NewGuid(),
            RootWorkId = rootWorkId,
            MediaType = "TV",
            CreatedAt = createdAt,
            Title = $"{showName} S{season}E{episode}",
            ShowName = showName,
            SeasonNumber = season,
            EpisodeNumber = episode,
            BackgroundSmallUrl = episodeBackgroundUrl,
            RootBackgroundUrl = "/shows/foundation-bg.jpg",
            RootBackgroundSmallUrl = "/shows/foundation-bg-s.jpg",
            RootBackgroundWidthPx = "1920",
            RootBackgroundHeightPx = "1080",
        };
}
