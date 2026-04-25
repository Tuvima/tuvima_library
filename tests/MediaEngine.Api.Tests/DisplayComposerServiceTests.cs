using MediaEngine.Api.Services.Display;

namespace MediaEngine.Api.Tests;

public sealed class DisplayComposerServiceTests
{
    [Fact]
    public async Task WatchLane_ComposesContinueMovieAndTvShelvesWithProgress()
    {
        var movieId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var tvId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var repository = new StubDisplayProjectionRepository(
            [
                Work(movieId, "Movie", "Arrival", year: "2016", genre: "Science Fiction|||Drama"),
                Work(tvId, "TV", "Pilot", year: "2008", genre: "Crime", season: "1", episode: "1"),
            ],
            [
                Journey(movieId, "Movie", "Arrival", progressPct: 42, year: "2016", genre: "Science Fiction|||Drama"),
            ]);
        var composer = new DisplayComposerService(repository, new DisplayCardBuilder());

        var page = await composer.BuildBrowseAsync("watch", null, "all", null, 0, 48);

        Assert.Equal("watch", page.Key);
        Assert.Contains(page.Shelves, shelf => shelf.Key == "continue-watching");
        Assert.Contains(page.Shelves, shelf => shelf.Key == "movies");
        Assert.Contains(page.Shelves, shelf => shelf.Key == "tv");

        var continueCard = page.Shelves.Single(shelf => shelf.Key == "continue-watching").Items.Single();
        Assert.Equal(42, continueCard.Progress?.Percent);
        Assert.Equal("Continue Watching", continueCard.Actions[0].Label);
        Assert.Equal(["2016", "Science Fiction", "Drama"], continueCard.Facts);
    }

    [Fact]
    public async Task ReadLane_ComposesBottomPreviewAndCompactBookFacts()
    {
        var bookId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var comicId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var repository = new StubDisplayProjectionRepository(
            [
                Work(bookId, "Book", "Dune", author: "Frank Herbert", genre: "Science Fiction;Adventure"),
                Work(comicId, "Comic", "Saga", author: "Brian K. Vaughan", genre: "Space Opera"),
            ],
            [
                Journey(bookId, "Book", "Dune", progressPct: 31, author: "Frank Herbert", genre: "Science Fiction;Adventure"),
            ]);
        var composer = new DisplayComposerService(repository, new DisplayCardBuilder());

        var page = await composer.BuildBrowseAsync("read", null, "all", null, 0, 48);

        Assert.Contains(page.Shelves, shelf => shelf.Key == "continue-reading");
        Assert.Contains(page.Shelves, shelf => shelf.Key == "recently-added");

        var continueCard = page.Shelves.Single(shelf => shelf.Key == "continue-reading").Items.Single();
        Assert.Equal("bottom", continueCard.PreviewPlacement);
        Assert.Equal("Continue Reading", continueCard.Actions[0].Label);
        Assert.Equal(["Frank Herbert", "Science Fiction", "Adventure"], continueCard.Facts);

        var catalogCard = page.Catalog.Single(card => card.Title == "Dune");
        Assert.Equal("bottom", catalogCard.PreviewPlacement);
        Assert.Equal(31, catalogCard.Progress?.Percent);
    }

    [Fact]
    public async Task ListenLane_ComposesMusicAudiobookAndContinueShelves()
    {
        var musicId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var audiobookId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var repository = new StubDisplayProjectionRepository(
            [
                Work(musicId, "Music", "Not Strong Enough", artist: "boygenius", album: "The Record", genre: "Indie Rock", track: "6"),
                Work(audiobookId, "Audiobook", "Project Hail Mary", author: "Andy Weir", narrator: "Ray Porter", genre: "Science Fiction"),
            ],
            [
                Journey(audiobookId, "Audiobook", "Project Hail Mary", progressPct: 58, author: "Andy Weir", narrator: "Ray Porter", genre: "Science Fiction"),
            ]);
        var composer = new DisplayComposerService(repository, new DisplayCardBuilder());

        var page = await composer.BuildBrowseAsync("listen", null, "all", null, 0, 48);

        Assert.Contains(page.Shelves, shelf => shelf.Key == "continue-listening");
        Assert.Contains(page.Shelves, shelf => shelf.Key == "music");
        Assert.Contains(page.Shelves, shelf => shelf.Key == "audiobooks");

        var musicCard = page.Shelves.Single(shelf => shelf.Key == "music").Items.Single();
        Assert.Equal(["boygenius", "The Record", "Track 6", "Indie Rock"], musicCard.Facts);
        Assert.Equal("square", musicCard.PreferredShape);

        var audiobookCard = page.Shelves.Single(shelf => shelf.Key == "continue-listening").Items.Single();
        Assert.Equal(58, audiobookCard.Progress?.Percent);
        Assert.Equal("Continue Listening", audiobookCard.Actions[0].Label);
    }

    [Fact]
    public async Task BrowseLane_CanOmitCatalogToAvoidDuplicatingShelfCards()
    {
        var movieId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var repository = new StubDisplayProjectionRepository(
            [Work(movieId, "Movie", "Arrival", year: "2016", genre: "Science Fiction")],
            []);
        var composer = new DisplayComposerService(repository, new DisplayCardBuilder());

        var page = await composer.BuildBrowseAsync("watch", null, "all", null, 0, 48, includeCatalog: false);

        Assert.Empty(page.Catalog);
        Assert.NotEmpty(page.Shelves);
        Assert.Contains(page.Shelves, shelf => shelf.Items.Any(card => card.Title == "Arrival"));
    }

    private static DisplayWorkRow Work(
        Guid workId,
        string mediaType,
        string title,
        string? author = null,
        string? artist = null,
        string? album = null,
        string? year = null,
        string? genre = null,
        string? narrator = null,
        string? season = null,
        string? episode = null,
        string? track = null)
    {
        return new DisplayWorkRow
        {
            WorkId = workId,
            AssetId = Guid.NewGuid(),
            MediaType = mediaType,
            CreatedAt = DateTimeOffset.Parse("2026-04-24T12:00:00Z"),
            Title = title,
            Author = author,
            Artist = artist,
            Album = album,
            Year = year,
            Genre = genre,
            Narrator = narrator,
            SeasonNumber = season,
            EpisodeNumber = episode,
            TrackNumber = track,
        };
    }

    private static DisplayJourneyRow Journey(
        Guid workId,
        string mediaType,
        string title,
        double progressPct,
        string? author = null,
        string? artist = null,
        string? album = null,
        string? year = null,
        string? genre = null,
        string? narrator = null,
        string? season = null,
        string? episode = null,
        string? track = null)
    {
        return new DisplayJourneyRow
        {
            WorkId = workId,
            AssetId = Guid.NewGuid(),
            MediaType = mediaType,
            ProgressPct = progressPct,
            LastAccessed = DateTimeOffset.Parse("2026-04-24T13:00:00Z"),
            Title = title,
            Author = author,
            Artist = artist,
            Album = album,
            Year = year,
            Genre = genre,
            Narrator = narrator,
            SeasonNumber = season,
            EpisodeNumber = episode,
            TrackNumber = track,
        };
    }

    private sealed class StubDisplayProjectionRepository : IDisplayProjectionRepository
    {
        private readonly IReadOnlyList<DisplayWorkRow> _works;
        private readonly IReadOnlyList<DisplayJourneyRow> _journey;

        public StubDisplayProjectionRepository(IReadOnlyList<DisplayWorkRow> works, IReadOnlyList<DisplayJourneyRow> journey)
        {
            _works = works;
            _journey = journey;
        }

        public Task<IReadOnlyList<DisplayWorkRow>> LoadWorksAsync(CancellationToken ct) =>
            Task.FromResult(_works);

        public Task<IReadOnlyList<DisplayJourneyRow>> LoadJourneyAsync(string? lane, CancellationToken ct)
        {
            var filtered = DisplayMediaRules.NormalizeLane(lane) switch
            {
                "watch" => _journey.Where(item => DisplayMediaRules.IsWatchKind(item.MediaType)),
                "read" => _journey.Where(item => DisplayMediaRules.IsReadKind(item.MediaType)),
                "listen" => _journey.Where(item => DisplayMediaRules.IsListenKind(item.MediaType)),
                _ => _journey,
            };

            return Task.FromResult<IReadOnlyList<DisplayJourneyRow>>(filtered.ToList());
        }
    }
}
