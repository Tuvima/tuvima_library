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
        var composer = CreateComposer(repository);

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
        var audiobookId = Guid.Parse("44444444-5555-5555-5555-444444444444");
        var repository = new StubDisplayProjectionRepository(
            [
                Work(bookId, "Book", "Dune", author: "Frank Herbert", genre: "Science Fiction;Adventure"),
                Work(comicId, "Comic", "Saga", author: "Brian K. Vaughan", genre: "Space Opera"),
                Work(audiobookId, "Audiobook", "Project Hail Mary", author: "Andy Weir", narrator: "Ray Porter", genre: "Science Fiction"),
            ],
            [
                Journey(bookId, "Book", "Dune", progressPct: 31, author: "Frank Herbert", genre: "Science Fiction;Adventure"),
            ]);
        var composer = CreateComposer(repository);

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

        Assert.Equal("bottom", page.Catalog.Single(card => card.Title == "Saga").PreviewPlacement);
        Assert.Equal("bottom", page.Catalog.Single(card => card.Title == "Project Hail Mary").PreviewPlacement);
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
        var composer = CreateComposer(repository);

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
        var composer = CreateComposer(repository);

        var page = await composer.BuildBrowseAsync("watch", null, "all", null, 0, 48, includeCatalog: false);

        Assert.Empty(page.Catalog);
        Assert.NotEmpty(page.Shelves);
        Assert.Contains(page.Shelves, shelf => shelf.Items.Any(card => card.Title == "Arrival"));
    }

    [Fact]
    public async Task BrowseResults_UseCatalogOnlyPayloadByDefault()
    {
        var movieId = Guid.Parse("77777777-8888-8888-8888-777777777777");
        var repository = new StubDisplayProjectionRepository(
            [Work(movieId, "Movie", "Arrival", year: "2016", genre: "Science Fiction")],
            []);
        var composer = CreateComposer(repository);

        var page = await composer.BuildBrowseAsync("watch", "Movie", "all", null, 0, 48, includeCatalog: true);

        Assert.Empty(page.Shelves);
        Assert.Single(page.Catalog);
        Assert.Equal("Arrival", page.Catalog[0].Title);
    }

    [Fact]
    public async Task ShelfPage_ReturnsCursorPagedShelfItems()
    {
        var firstId = Guid.Parse("77777777-1111-1111-1111-777777777777");
        var secondId = Guid.Parse("77777777-2222-2222-2222-777777777777");
        var repository = new StubDisplayProjectionRepository(
            [
                Work(firstId, "Movie", "Arrival", year: "2016", genre: "Science Fiction"),
                Work(secondId, "Movie", "Heat", year: "1995", genre: "Crime"),
            ],
            []);
        var composer = CreateComposer(repository);

        var page = await composer.BuildShelfPageAsync(
            "movies",
            lane: "watch",
            mediaType: null,
            grouping: "all",
            search: null,
            cursor: null,
            offset: 0,
            limit: 1);

        Assert.NotNull(page);
        Assert.Equal("movies", page.Shelf.Key);
        Assert.Single(page.Shelf.Items);
        Assert.Equal("1", page.NextCursor);
        Assert.Equal(0, page.Offset);
        Assert.Equal(1, page.Limit);
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task MusicHome_ComposesStreamingStyleShelvesFromDisplayApi()
    {
        var albumId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var firstTrack = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var secondTrack = Guid.Parse("aaaaaaaa-9999-9999-9999-aaaaaaaaaaaa");
        var profileId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var repository = new StubDisplayProjectionRepository(
            [
                Work(firstTrack, "Music", "Static", artist: "Among The Outcasts", album: "Static On The Line", genre: "Rock", track: "1", collectionId: albumId),
                Work(secondTrack, "Music", "Signals", artist: "Among The Outcasts", album: "Static On The Line", genre: "Rock", track: "2", collectionId: albumId),
            ],
            [
                Journey(secondTrack, "Music", "Signals", progressPct: 44, artist: "Among The Outcasts", album: "Static On The Line", genre: "Rock", track: "2", collectionId: albumId),
            ],
            new HashSet<Guid> { firstTrack });
        var composer = CreateComposer(repository);

        var page = await composer.BuildBrowseAsync("listen", "Music", "home", null, 0, 48, includeCatalog: true, profileId: profileId);

        Assert.Equal("listen-music", page.Key);
        Assert.Contains(page.Shelves, shelf => shelf.Key == "recently-played");
        Assert.Contains(page.Shelves, shelf => shelf.Key == "favorite-songs");
        Assert.Contains(page.Shelves, shelf => shelf.Key == "recently-added");
        Assert.Contains(page.Shelves, shelf => shelf.Key == "albums");
        Assert.Contains(page.Shelves, shelf => shelf.Key == "artists");

        var favoriteCard = page.Shelves.Single(shelf => shelf.Key == "favorite-songs").Items.Single();
        Assert.Equal(firstTrack, favoriteCard.WorkId);
        Assert.True(favoriteCard.Flags.IsFavorite);

        var albumCard = page.Shelves.Single(shelf => shelf.Key == "albums").Items.Single();
        Assert.Equal("Static On The Line", albumCard.Title);
        Assert.Equal("album", albumCard.GroupingType);
        Assert.Equal("album", albumCard.Presentation);
        Assert.Equal("square", albumCard.PreferredShape);
        Assert.Equal($"/listen/music/albums/{albumId:D}", albumCard.Actions[0].WebUrl);

        var artistCard = page.Shelves.Single(shelf => shelf.Key == "artists").Items.Single();
        Assert.Equal("Among The Outcasts", artistCard.Title);
        Assert.Equal("artist", artistCard.GroupingType);
        Assert.Equal("artist", artistCard.Presentation);
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
        string? track = null,
        Guid? collectionId = null)
    {
        return new DisplayWorkRow
        {
            WorkId = workId,
            AssetId = Guid.NewGuid(),
            CollectionId = collectionId,
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
        string? track = null,
        Guid? collectionId = null)
    {
        return new DisplayJourneyRow
        {
            WorkId = workId,
            AssetId = Guid.NewGuid(),
            CollectionId = collectionId,
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

    private static DisplayComposerService CreateComposer(IDisplayProjectionRepository repository)
    {
        var cards = new DisplayCardBuilder();
        return new DisplayComposerService(repository, cards, new DisplayShelfBuilder(cards));
    }

    private sealed class StubDisplayProjectionRepository : IDisplayProjectionRepository
    {
        private readonly IReadOnlyList<DisplayWorkRow> _works;
        private readonly IReadOnlyList<DisplayJourneyRow> _journey;
        private readonly IReadOnlySet<Guid> _favoriteWorkIds;

        public StubDisplayProjectionRepository(
            IReadOnlyList<DisplayWorkRow> works,
            IReadOnlyList<DisplayJourneyRow> journey,
            IReadOnlySet<Guid>? favoriteWorkIds = null)
        {
            _works = works;
            _journey = journey;
            _favoriteWorkIds = favoriteWorkIds ?? new HashSet<Guid>();
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

        public Task<IReadOnlySet<Guid>> LoadFavoriteWorkIdsAsync(Guid? profileId, CancellationToken ct) =>
            Task.FromResult(profileId.HasValue ? _favoriteWorkIds : new HashSet<Guid>());
    }
}
