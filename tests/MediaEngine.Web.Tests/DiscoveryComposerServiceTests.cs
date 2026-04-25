using MediaEngine.Contracts.Display;
using MediaEngine.Domain;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Discovery;

namespace MediaEngine.Web.Tests;

public sealed class DiscoveryComposerServiceTests
{
    [Fact]
    public void FromDisplayCard_PreservesCompactDisplayHintsAndProgress()
    {
        var assetId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var workId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var resumeAction = new DisplayActionDto("readAsset", "Continue Reading", WorkId: workId, AssetId: assetId, WebUrl: $"/read/{assetId}");
        var card = new DisplayCardDto(
            Id: workId,
            WorkId: workId,
            AssetId: assetId,
            CollectionId: null,
            MediaType: "Book",
            GroupingType: "work",
            Title: "Dune",
            Subtitle: "Frank Herbert",
            Facts: ["Frank Herbert", "Science Fiction"],
            Artwork: new DisplayArtworkDto(
                CoverUrl: "http://localhost:61495/stream/11111111-1111-1111-1111-111111111111/cover",
                SquareUrl: null,
                BannerUrl: null,
                BackgroundUrl: null,
                LogoUrl: null,
                CoverWidthPx: 1000,
                CoverHeightPx: 1500,
                SquareWidthPx: null,
                SquareHeightPx: null,
                BannerWidthPx: null,
                BannerHeightPx: null,
                BackgroundWidthPx: null,
                BackgroundHeightPx: null,
                AccentColor: "#5DCAA5"),
            PreferredShape: "portrait",
            Presentation: "book",
            TileTextMode: "coverOnly",
            PreviewPlacement: "bottom",
            Progress: new DisplayProgressDto(32, "32%", DateTimeOffset.Parse("2026-04-24T12:00:00Z"), resumeAction),
            Actions: [resumeAction, new DisplayActionDto("openWork", "Details", WorkId: workId, WebUrl: $"/book/{workId}")],
            Flags: new DisplayCardFlagsDto(IsPlayable: false, IsReadable: true, CanAddToCollection: true, IsCollection: false, IsFavorite: false),
            SortTimestamp: DateTimeOffset.Parse("2026-04-24T12:00:00Z"));

        var mapped = DiscoveryComposerService.FromDisplayCard(card);

        Assert.Equal(DiscoveryTileTextMode.CoverOnly, mapped.TileTextMode);
        Assert.Equal(DiscoveryPreviewPlacement.Bottom, mapped.PreviewPlacement);
        Assert.Equal(["Frank Herbert", "Science Fiction"], mapped.HoverFacts);
        Assert.Equal(32, mapped.ProgressPct);
        Assert.Equal($"/read/{assetId}", mapped.PrimaryNavigationUrl);
        Assert.Equal("Continue Reading", mapped.PrimaryActionLabel);
    }

    [Fact]
    public void FromDisplayPage_UsesDisplayHeroAndShelvesWithoutLegacyComposition()
    {
        var workId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var action = new DisplayActionDto("playAsset", "Play", WorkId: workId, WebUrl: $"/watch/movies/{workId}");
        var card = new DisplayCardDto(
            Id: workId,
            WorkId: workId,
            AssetId: null,
            CollectionId: null,
            MediaType: "Movie",
            GroupingType: "work",
            Title: "Arrival",
            Subtitle: null,
            Facts: ["2016", "Science Fiction"],
            Artwork: new DisplayArtworkDto("/cover.jpg", null, null, "/background.jpg", null, null, null, null, null, null, null, null, null, "#60A5FA"),
            PreferredShape: "landscape",
            Presentation: "movie",
            TileTextMode: "caption",
            PreviewPlacement: "smart",
            Progress: null,
            Actions: [action],
            Flags: new DisplayCardFlagsDto(true, false, true, false, false),
            SortTimestamp: DateTimeOffset.Parse("2026-04-24T12:00:00Z"));
        var page = new DisplayPageDto(
            Key: "watch",
            Title: "Watch",
            Subtitle: "Movies and shows",
            Hero: new DisplayHeroDto("Arrival", null, "Featured from your library", card.Artwork, null, [action]),
            Shelves: [new DisplayShelfDto("movies", "Movies in your library", null, [card], null)],
            Catalog: [card]);

        var mapped = DiscoveryComposerService.FromDisplayPage(page);

        Assert.Equal("watch", mapped.Key);
        Assert.Equal("Arrival", mapped.Hero?.Title);
        Assert.Single(mapped.Shelves);
        Assert.Single(mapped.Catalog);
        Assert.Equal(["2016", "Science Fiction"], mapped.Catalog[0].HoverFacts);
    }

    [Fact]
    public void ComposeListen_CreatesDistinctMusicAndAudiobookShelves()
    {
        var service = new DiscoveryComposerService(null!);

        var works = new[]
        {
            CreateWork(
                id: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                mediaType: "Music",
                title: "The Record",
                creator: "Boygenius",
                year: "2023"),
            CreateWork(
                id: Guid.Parse("22222222-2222-2222-2222-222222222222"),
                mediaType: "Audiobooks",
                title: "Project Hail Mary",
                creator: "Andy Weir",
                year: "2021")
        };

        var page = service.ComposeListen(works, [], []);

        Assert.Contains(page.Shelves, shelf => shelf.Title == "New music in your library");
        Assert.Contains(page.Shelves, shelf => shelf.Title == "Audiobooks on deck");
    }

    [Fact]
    public void ComposeMusicHome_CreatesMusicLandingShelves()
    {
        var service = new DiscoveryComposerService(null!);
        var albumId = Guid.Parse("aaaaaaaa-1111-1111-1111-aaaaaaaaaaaa");

        var works = new[]
        {
            CreateWork(
                id: Guid.Parse("bbbbbbbb-1111-1111-1111-bbbbbbbbbbbb"),
                mediaType: "Music",
                title: "Static",
                creator: "Among The Outcasts",
                year: "2026",
                collectionId: albumId,
                canonicalExtras: new Dictionary<string, string>
                {
                    ["artist"] = "Among The Outcasts",
                    ["album"] = "Static On The Line",
                    ["genre"] = "Rock",
                })
        };

        var journey = new[]
        {
            CreateJourneyItem(
                workId: works[0].Id,
                assetId: Guid.Parse("cccccccc-1111-1111-1111-cccccccccccc"),
                mediaType: "Music",
                title: "Static",
                author: "Among The Outcasts",
                collectionId: albumId,
                collectionDisplayName: "Static On The Line",
                lastAccessed: new DateTimeOffset(2026, 4, 21, 8, 0, 0, TimeSpan.Zero),
                coverUrl: "/art/static.jpg")
        };

        var albums = new[]
        {
            new ContentGroupViewModel
            {
                CollectionId = albumId,
                DisplayName = "Static On The Line",
                PrimaryMediaType = "Music",
                Creator = "Among The Outcasts",
                CoverUrl = "/art/static.jpg",
                WorkCount = 1,
                CreatedAt = new DateTimeOffset(2026, 4, 21, 0, 0, 0, TimeSpan.Zero),
            }
        };

        var artists = new[]
        {
            new ContentGroupViewModel
            {
                CollectionId = Guid.Parse("dddddddd-1111-1111-1111-dddddddddddd"),
                DisplayName = "Among The Outcasts",
                PrimaryMediaType = "Music",
                ArtistPhotoUrl = "/art/artist.jpg",
                WorkCount = 1,
                CreatedAt = new DateTimeOffset(2026, 4, 21, 0, 0, 0, TimeSpan.Zero),
            }
        };

        var page = service.ComposeMusicHome(works, journey, albums, artists, [works[0].Id]);

        Assert.Equal("listen-music", page.Key);
        Assert.Contains(page.Shelves, shelf => shelf.Title == "Recently Played");
        Assert.Contains(page.Shelves, shelf => shelf.Title == "Favorite Songs");
        Assert.Contains(page.Shelves, shelf => shelf.Title == "Albums");
        Assert.Contains(page.Shelves, shelf => shelf.Title == "Artists");
    }

    [Fact]
    public void ComposeHome_UsesSafeSeparatorsInHeroAndCollectionDescriptions()
    {
        var service = new DiscoveryComposerService(null!);

        var groups = new[]
        {
            new ContentGroupViewModel
            {
                CollectionId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                DisplayName = "The Expanse",
                PrimaryMediaType = "TV",
                WorkCount = 10,
                SeasonCount = 2,
                CreatedAt = DateTimeOffset.UtcNow,
            }
        };

        var page = service.ComposeHome([], [], groups);

        Assert.Equal("2 seasons / 10 items", page.Hero?.Description);
        Assert.DoesNotContain("Ã", page.Hero?.Description);
    }

    [Fact]
    public void ComposeHome_SplitsHomeRowsBySurfaceAndKeepsContinueHeroFirst()
    {
        var service = new DiscoveryComposerService(null!);
        var tvCollectionId = Guid.Parse("10101010-1010-1010-1010-101010101010");
        var albumCollectionId = Guid.Parse("20202020-2020-2020-2020-202020202020");
        var movieId = Guid.Parse("30303030-3030-3030-3030-303030303030");
        var latestEpisodeId = Guid.Parse("40404040-4040-4040-4040-404040404040");

        var works = new[]
        {
            CreateWork(
                id: Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
                mediaType: "TV",
                title: "Pilot",
                creator: "Kevin Hart",
                year: "2026",
                collectionId: tvCollectionId,
                createdAt: new DateTimeOffset(2026, 4, 18, 12, 0, 0, TimeSpan.Zero),
                coverUrl: "/art/episode-cover.jpg",
                backgroundUrl: "/art/episode-background.jpg",
                bannerUrl: "/art/episode-banner.jpg",
                canonicalExtras: new Dictionary<string, string>
                {
                    ["series"] = "Funny AF",
                    ["show_name"] = "Funny AF with Kevin Hart",
                    ["season_number"] = "2",
                    ["episode_number"] = "3",
                    ["description"] = "Episode one description",
                }),
            CreateWork(
                id: latestEpisodeId,
                mediaType: "TV",
                title: "Auditions: Chicago",
                creator: "Kevin Hart",
                year: "2026",
                collectionId: tvCollectionId,
                createdAt: new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero),
                coverUrl: "/art/episode-cover-2.jpg",
                backgroundUrl: "/art/episode-background-2.jpg",
                bannerUrl: "/art/episode-banner-2.jpg",
                canonicalExtras: new Dictionary<string, string>
                {
                    ["series"] = "Funny AF",
                    ["show_name"] = "Funny AF with Kevin Hart",
                    ["season_number"] = "2",
                    ["episode_number"] = "4",
                    ["tldr"] = "A stand-up competition episode with sharp backstage chaos.",
                    ["vibe"] = "High-energy",
                    ["mood"] = "Playful",
                    ["description"] = "Episode two description",
                }),
            CreateWork(
                id: Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"),
                mediaType: "Music",
                title: "Track One",
                creator: "boygenius",
                year: "2023",
                collectionId: albumCollectionId,
                createdAt: new DateTimeOffset(2026, 4, 17, 12, 0, 0, TimeSpan.Zero),
                coverUrl: "/art/album-cover.jpg",
                backgroundUrl: "/art/album-background.jpg",
                bannerUrl: "/art/album-banner.jpg",
                logoUrl: "/art/album-logo.png",
                canonicalExtras: new Dictionary<string, string>
                {
                    ["artist"] = "boygenius",
                    ["album"] = "The Record",
                    ["track_number"] = "1",
                    ["description"] = "Album track one",
                }),
            CreateWork(
                id: Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002"),
                mediaType: "Music",
                title: "Track Two",
                creator: "boygenius",
                year: "2023",
                collectionId: albumCollectionId,
                createdAt: new DateTimeOffset(2026, 4, 17, 13, 0, 0, TimeSpan.Zero),
                coverUrl: "/art/album-cover.jpg",
                backgroundUrl: "/art/album-background.jpg",
                bannerUrl: "/art/album-banner.jpg",
                logoUrl: "/art/album-logo.png",
                canonicalExtras: new Dictionary<string, string>
                {
                    ["artist"] = "boygenius",
                    ["album"] = "The Record",
                    ["track_number"] = "2",
                    ["description"] = "Album track two",
                }),
            CreateWork(
                id: movieId,
                mediaType: "Movies",
                title: "Anaconda",
                creator: "Tom Gormican",
                year: "2025",
                createdAt: new DateTimeOffset(2026, 4, 16, 12, 0, 0, TimeSpan.Zero),
                coverUrl: "/art/movie-cover.jpg",
                canonicalExtras: new Dictionary<string, string>
                {
                    ["director"] = "Tom Gormican",
                    ["description"] = "A movie that should stay individual on Home.",
                }),
        };

        var journey = new[]
        {
            CreateJourneyItem(
                workId: latestEpisodeId,
                assetId: Guid.Parse("50505050-5050-5050-5050-505050505050"),
                mediaType: "TV",
                title: "Auditions: Chicago",
                author: "Kevin Hart",
                collectionId: tvCollectionId,
                collectionDisplayName: "Funny AF with Kevin Hart",
                lastAccessed: new DateTimeOffset(2026, 4, 20, 10, 0, 0, TimeSpan.Zero),
                coverUrl: "/art/episode-cover-2.jpg",
                backgroundUrl: "/art/episode-background-2.jpg",
                bannerUrl: "/art/episode-banner-2.jpg",
                heroUrl: "/art/episode-hero-2.jpg",
                extendedProperties: new Dictionary<string, string>
                {
                    ["season_number"] = "2",
                    ["episode_number"] = "4",
                }),
            CreateJourneyItem(
                workId: Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002"),
                assetId: Guid.Parse("60606060-6060-6060-6060-606060606060"),
                mediaType: "Music",
                title: "Track Two",
                author: "boygenius",
                collectionId: albumCollectionId,
                collectionDisplayName: "The Record",
                lastAccessed: new DateTimeOffset(2026, 4, 19, 10, 0, 0, TimeSpan.Zero),
                coverUrl: "/art/album-cover.jpg",
                backgroundUrl: "/art/album-background.jpg",
                bannerUrl: "/art/album-banner.jpg",
                logoUrl: "/art/album-logo.png",
                extendedProperties: new Dictionary<string, string>
                {
                    ["track_number"] = "2",
                }),
            CreateJourneyItem(
                workId: movieId,
                assetId: Guid.Parse("70707070-7070-7070-7070-707070707070"),
                mediaType: "Movies",
                title: "Anaconda",
                author: "Tom Gormican",
                lastAccessed: new DateTimeOffset(2026, 4, 18, 9, 0, 0, TimeSpan.Zero),
                coverUrl: "/art/movie-cover.jpg"),
        };

        var groups = new[]
        {
            new ContentGroupViewModel
            {
                CollectionId = tvCollectionId,
                DisplayName = "Funny AF with Kevin Hart",
                PrimaryMediaType = "TV",
                WorkCount = 6,
                SeasonCount = 2,
                Network = "Netflix",
                Year = "2026",
                CoverUrl = "/art/show-cover.jpg",
                BackgroundUrl = "/art/show-background.jpg",
                BannerUrl = "/art/show-banner.jpg",
                LogoUrl = "/art/show-logo.png",
                Description = "A competition series for comics.",
                CreatedAt = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
            }
        };

        var page = service.ComposeHome(
            works,
            journey,
            groups,
            groupPreviewImages: new Dictionary<Guid, IReadOnlyList<string>>
            {
                [tvCollectionId] = ["/art/preview-1.jpg", "/art/preview-2.jpg"]
            });

        Assert.Equal("Continue with your library", page.Hero?.Eyebrow);
        Assert.Equal("Funny AF with Kevin Hart", page.Hero?.Title);
        Assert.Equal("/art/show-background.jpg", page.Hero?.BackgroundImageUrl);
        Assert.Equal("/art/show-logo.png", page.Hero?.LogoUrl);
        Assert.Equal("Continue watching", page.Hero?.PrimaryActionLabel);
        Assert.Equal($"/watch/tv/show/{tvCollectionId}/episode/{latestEpisodeId}", page.Hero?.PrimaryNavigationUrl);
        Assert.Equal($"/watch/tv/show/{tvCollectionId}", page.Hero?.SecondaryNavigationUrl);
        Assert.Equal("A stand-up competition episode with sharp backstage chaos.", page.Hero?.Tldr);
        Assert.Contains("High-energy", page.Hero?.VibeTags ?? []);

        Assert.Collection(
            page.Shelves.Take(5),
            shelf => Assert.Equal("Continue Watching", shelf.Title),
            shelf => Assert.Equal("Fresh to Watch", shelf.Title),
            shelf => Assert.Equal("Watch Posters", shelf.Title),
            shelf => Assert.Equal("Continue Listening", shelf.Title),
            shelf => Assert.Equal("Fresh Music", shelf.Title));

        var continueWatchShelf = page.Shelves[0];
        var continueTv = Assert.Single(continueWatchShelf.Items, item => item.Title == "Funny AF with Kevin Hart");
        Assert.Equal(DiscoveryCardPresentation.TvSeries, continueTv.Presentation);
        Assert.Equal(DiscoveryCardShape.Landscape, continueTv.Shape);
        Assert.Equal(DiscoverySurfaceKind.BannerLandscape, continueTv.SurfaceKind);
        Assert.Equal(DiscoveryHoverLayout.BannerPopover, continueTv.HoverLayout);
        Assert.Equal("Continue watching", continueTv.PrimaryActionLabel);
        Assert.Equal($"/watch/tv/show/{tvCollectionId}", continueTv.NavigationUrl);
        Assert.Equal($"/watch/tv/show/{tvCollectionId}/episode/{latestEpisodeId}", continueTv.PrimaryNavigationUrl);
        Assert.Contains("Continue S2:E4", continueTv.StatusText, StringComparison.Ordinal);
        Assert.Equal("A stand-up competition episode with sharp backstage chaos.", continueTv.Tldr);
        Assert.Contains("High-energy", continueTv.VibeTags);

        var freshWatchShelf = page.Shelves[1];
        var freshTv = Assert.Single(freshWatchShelf.Items, item => item.Title == "Funny AF with Kevin Hart");
        Assert.Equal("2 new episodes", freshTv.StatusText);
        Assert.Equal("/art/show-logo.png", freshTv.LogoUrl);
        Assert.Equal("/art/show-background.jpg", freshTv.TileImageUrl);

        var watchPosterShelf = page.Shelves[2];
        var continueMovie = Assert.Single(watchPosterShelf.Items, item => item.Title == "Anaconda");
        Assert.False(continueMovie.IsCollection);
        Assert.Equal(DiscoveryCardShape.Portrait, continueMovie.Shape);
        Assert.Equal(DiscoverySurfaceKind.CoverPortrait, continueMovie.SurfaceKind);
        Assert.Equal(DiscoveryHoverLayout.ArtOnlyPopover, continueMovie.HoverLayout);

        var continueMusicShelf = page.Shelves[3];
        var continueAlbum = Assert.Single(continueMusicShelf.Items, item => item.Title == "The Record");
        Assert.Equal(DiscoveryCardPresentation.Album, continueAlbum.Presentation);
        Assert.Equal(DiscoveryCardShape.Square, continueAlbum.Shape);
        Assert.Equal("Continue album", continueAlbum.PrimaryActionLabel);
        Assert.Equal($"/listen/music/albums/{albumCollectionId}", continueAlbum.NavigationUrl);

        var freshMusicShelf = page.Shelves[4];
        var freshAlbum = Assert.Single(freshMusicShelf.Items, item => item.Title == "The Record");
        Assert.Equal("2 new tracks", freshAlbum.StatusText);
        Assert.Equal(DiscoveryCardShape.Square, freshAlbum.Shape);
        Assert.Equal(DiscoverySurfaceKind.CoverSquare, freshAlbum.SurfaceKind);

        Assert.All(
            page.Shelves.Take(5).Where(shelf => shelf.Items.Count > 0),
            shelf => Assert.Single(shelf.Items.Select(item => item.Shape).Distinct()));

        Assert.DoesNotContain(page.Catalog, item => item.Title == "Pilot");
        Assert.DoesNotContain(page.Catalog, item => item.Title == "Auditions: Chicago");
        Assert.DoesNotContain(page.Catalog, item => item.Title == "Track One");
        Assert.DoesNotContain(page.Catalog, item => item.Title == "Track Two");
        Assert.Contains(page.Catalog, item => item.Title == "Anaconda");
    }

    [Fact]
    public void ComposeHome_PopulatesAiFieldsForCoverLedMedia()
    {
        var service = new DiscoveryComposerService(null!);
        var work = CreateWork(
            id: Guid.Parse("12121212-3434-5656-7878-909090909090"),
            mediaType: "Books",
            title: "Project Hail Mary",
            creator: "Andy Weir",
            year: "2021",
            coverUrl: "/art/hail-mary.jpg",
            canonicalExtras: new Dictionary<string, string>
            {
                ["tldr"] = "A lone astronaut tries to save Earth.",
                ["vibe"] = "Hopeful",
                ["mood"] = "Tense",
                ["genre"] = "Science Fiction",
            });

        var page = service.ComposeHome([work], [], []);
        var freshReads = Assert.Single(page.Shelves, shelf => shelf.Title == "Fresh Reads");
        var card = Assert.Single(freshReads.Items);

        Assert.Equal("A lone astronaut tries to save Earth.", card.Tldr);
        Assert.Equal(DiscoveryCardShape.Portrait, card.Shape);
        Assert.Equal(DiscoverySurfaceKind.CoverPortrait, card.SurfaceKind);
        Assert.Equal(DiscoveryHoverLayout.ArtOnlyPopover, card.HoverLayout);
        Assert.Contains("Hopeful", card.VibeTags);
        Assert.Contains("Tense", card.VibeTags);
    }

    [Fact]
    public void ComposeHome_UsesLandscapeSurfaceForMoviesWithFanart()
    {
        var service = new DiscoveryComposerService(null!);
        var movie = CreateWork(
            id: Guid.Parse("abababab-1111-2222-3333-444444444444"),
            mediaType: "Movies",
            title: "Interstellar",
            creator: "Christopher Nolan",
            year: "2014",
            coverUrl: "/art/interstellar-poster.jpg",
            backgroundUrl: "/art/interstellar-fanart.jpg");

        var page = service.ComposeHome([movie], [], []);
        var freshWatch = Assert.Single(page.Shelves, shelf => shelf.Title == "Fresh to Watch");
        var card = Assert.Single(freshWatch.Items);

        Assert.Equal(DiscoveryCardShape.Landscape, card.Shape);
        Assert.Equal(DiscoverySurfaceKind.BannerLandscape, card.SurfaceKind);
        Assert.Equal(DiscoveryHoverLayout.BannerPopover, card.HoverLayout);
        Assert.Equal("/art/interstellar-fanart.jpg", card.TileImageUrl);
    }

    [Fact]
    public void ComposeHome_AffinityRowsKeepResolvedArtworkShapes()
    {
        var service = new DiscoveryComposerService(null!);
        var movie = CreateWork(
            id: Guid.Parse("abababab-3333-4444-5555-666666666666"),
            mediaType: "Movies",
            title: "Foundation",
            creator: "David S. Goyer",
            year: "2021",
            coverUrl: "/art/foundation-poster.jpg",
            backgroundUrl: "/art/foundation-background.jpg");
        var song = CreateWork(
            id: Guid.Parse("bcbcbcbc-3333-4444-5555-666666666666"),
            mediaType: "Music",
            title: "Clair de Lune",
            creator: "Claude Debussy",
            year: "1905",
            coverUrl: "/art/debussy-cover.jpg");

        var page = service.ComposeHome([movie, song], [], []);

        var watchNext = Assert.Single(page.Shelves, shelf => shelf.Title == "Watch next");
        var watchCard = Assert.Single(watchNext.Items);
        Assert.Equal(DiscoveryCardShape.Landscape, watchCard.Shape);
        Assert.Equal(DiscoverySurfaceKind.BannerLandscape, watchCard.SurfaceKind);
        Assert.Equal(DiscoveryHoverLayout.BannerPopover, watchCard.HoverLayout);

        var listenNext = Assert.Single(page.Shelves, shelf => shelf.Title == "Listen next");
        var listenCard = Assert.Single(listenNext.Items);
        Assert.Equal(DiscoveryCardShape.Square, listenCard.Shape);
        Assert.Equal(DiscoverySurfaceKind.CoverSquare, listenCard.SurfaceKind);
        Assert.Equal(DiscoveryHoverLayout.ArtOnlyPopover, listenCard.HoverLayout);
    }

    [Fact]
    public void ComposeHome_DoesNotUsePosterAsLandscapeTile()
    {
        var service = new DiscoveryComposerService(null!);
        var movie = CreateWork(
            id: Guid.Parse("abababab-2222-3333-4444-555555555555"),
            mediaType: "Movies",
            title: "Arrival",
            creator: "Denis Villeneuve",
            year: "2016",
            coverUrl: "/art/arrival-poster.jpg");

        var page = service.ComposeHome([movie], [], []);
        var posterShelf = Assert.Single(page.Shelves, shelf => shelf.Title == "Watch Posters");
        var card = Assert.Single(posterShelf.Items);

        Assert.Equal(DiscoveryCardShape.Portrait, card.Shape);
        Assert.Equal(DiscoverySurfaceKind.CoverPortrait, card.SurfaceKind);
        Assert.Equal("/art/arrival-poster.jpg", card.TileImageUrl);
        Assert.Null(card.BackgroundUrl);
        Assert.Null(card.BannerUrl);
    }

    [Fact]
    public void ComposeHome_DoesNotUseBackgroundAsPortraitTile()
    {
        var service = new DiscoveryComposerService(null!);
        var book = CreateWork(
            id: Guid.Parse("bcbcbcbc-2222-3333-4444-555555555555"),
            mediaType: "Books",
            title: "Posterless Book",
            creator: "An Author",
            year: "2026",
            backgroundUrl: "/art/book-background.jpg");

        var page = service.ComposeHome([book], [], []);
        var freshReads = Assert.Single(page.Shelves, shelf => shelf.Title == "Fresh Reads");
        var card = Assert.Single(freshReads.Items);

        Assert.Equal(DiscoveryCardShape.Portrait, card.Shape);
        Assert.Equal(DiscoverySurfaceKind.CoverPortrait, card.SurfaceKind);
        Assert.Null(card.TileImageUrl);
        Assert.Equal("/art/book-background.jpg", card.BackgroundUrl);
    }

    [Fact]
    public void ComposeHome_UsesPreviewStackForPosterlessTvSeriesWithoutWideArt()
    {
        var service = new DiscoveryComposerService(null!);
        var groupId = Guid.Parse("12345678-2222-3333-4444-555555555555");
        var group = new ContentGroupViewModel
        {
            CollectionId = groupId,
            DisplayName = "Breaking Bad",
            PrimaryMediaType = "TV",
            WorkCount = 62,
            SeasonCount = 5,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var page = service.ComposeHome(
            [],
            [],
            [group],
            groupPreviewImages: new Dictionary<Guid, IReadOnlyList<string>>
            {
                [groupId] = ["/art/breaking-bad-s1.jpg", "/art/breaking-bad-s2.jpg"]
            });

        var shelf = Assert.Single(page.Shelves, shelf => shelf.Title == "TV Series");
        var card = Assert.Single(shelf.Items);

        Assert.Equal(DiscoveryCardShape.Portrait, card.Shape);
        Assert.Equal(DiscoverySurfaceKind.CoverPortrait, card.SurfaceKind);
        Assert.Null(card.TileImageUrl);
        Assert.Equal(2, card.PreviewImages.Count);
    }

    [Fact]
    public void ComposeHome_UsesSquareCardsForAudiobooks()
    {
        var service = new DiscoveryComposerService(null!);
        var audiobook = CreateWork(
            id: Guid.Parse("cdcdcdcd-1111-2222-3333-444444444444"),
            mediaType: "Audiobooks",
            title: "Project Hail Mary",
            creator: "Andy Weir",
            year: "2021",
            coverUrl: "/art/hail-mary-square.jpg",
            canonicalExtras: new Dictionary<string, string>
            {
                ["cover_aspect_class"] = ArtworkAspectClasses.Square,
            });

        var page = service.ComposeHome([audiobook], [], []);
        var freshAudiobooks = Assert.Single(page.Shelves, shelf => shelf.Title == "Fresh Audiobooks");
        var card = Assert.Single(freshAudiobooks.Items);

        Assert.Equal(DiscoveryCardShape.Square, card.Shape);
        Assert.Equal(DiscoverySurfaceKind.CoverSquare, card.SurfaceKind);
    }

    [Fact]
    public void ComposeHome_UsesSquareCardsForAudiobooksWithSquareUrl()
    {
        var service = new DiscoveryComposerService(null!);
        var audiobook = CreateWork(
            id: Guid.Parse("dcdcdcdc-1111-2222-3333-444444444444"),
            mediaType: "Audiobooks",
            title: "Neuromancer",
            creator: "William Gibson",
            year: "1984",
            coverUrl: "/art/neuromancer-cover.jpg",
            canonicalExtras: new Dictionary<string, string>
            {
                ["square_url"] = "/art/neuromancer-square.jpg",
            });

        var page = service.ComposeHome([audiobook], [], []);
        var freshAudiobooks = Assert.Single(page.Shelves, shelf => shelf.Title == "Fresh Audiobooks");
        var card = Assert.Single(freshAudiobooks.Items);

        Assert.Equal(DiscoveryCardShape.Square, card.Shape);
        Assert.Equal(DiscoverySurfaceKind.CoverSquare, card.SurfaceKind);
        Assert.Equal("/art/neuromancer-square.jpg", card.TileImageUrl);
    }

    [Fact]
    public void ComposeHome_RendersAudiobookCoversInSquareTiles()
    {
        var service = new DiscoveryComposerService(null!);
        var audiobook = CreateWork(
            id: Guid.Parse("edededed-1111-2222-3333-444444444444"),
            mediaType: "Audiobooks",
            title: "The Hobbit",
            creator: "J.R.R. Tolkien",
            year: "1937",
            coverUrl: "/art/hobbit-cover.jpg",
            canonicalExtras: new Dictionary<string, string>
            {
                ["cover_aspect_class"] = ArtworkAspectClasses.Portrait,
            });

        var page = service.ComposeHome([audiobook], [], []);
        var freshAudiobooks = Assert.Single(page.Shelves, shelf => shelf.Title == "Fresh Audiobooks");
        var card = Assert.Single(freshAudiobooks.Items);

        Assert.Equal(DiscoveryCardShape.Square, card.Shape);
        Assert.Equal(DiscoverySurfaceKind.CoverSquare, card.SurfaceKind);
        Assert.Equal("/art/hobbit-cover.jpg", card.TileImageUrl);
        Assert.Equal(DiscoveryImageFitMode.Contain, card.TileImageFitMode);
    }

    [Fact]
    public void ComposeHome_HidesSingleItemCollectionShelves()
    {
        var service = new DiscoveryComposerService(null!);

        var groups = new[]
        {
            new ContentGroupViewModel
            {
                CollectionId = Guid.Parse("eeeeeeee-1111-2222-3333-444444444444"),
                DisplayName = "Studio Ghibli",
                PrimaryMediaType = "Movies",
                WorkCount = 1,
                CoverUrl = "/art/ghibli.jpg",
                CreatedAt = DateTimeOffset.UtcNow,
            }
        };

        var page = service.ComposeHome([], [], groups);

        Assert.DoesNotContain(page.Shelves, shelf => shelf.Title == "Movie Series");
        Assert.Null(page.Hero);
    }

    [Fact]
    public void ComposeHome_HidesAudiobookSeriesWhenGroupOnlyHasOneDistinctTitle()
    {
        var service = new DiscoveryComposerService(null!);

        var groups = new[]
        {
            new ContentGroupViewModel
            {
                CollectionId = Guid.Parse("eeeeeeee-2222-3333-4444-555555555555"),
                DisplayName = "Dune",
                PrimaryMediaType = "Audiobooks",
                WorkCount = 2,
                DistinctTitleCount = 1,
                CoverUrl = "/art/dune.jpg",
                CreatedAt = DateTimeOffset.UtcNow,
            }
        };

        var page = service.ComposeHome([], [], groups);

        Assert.DoesNotContain(page.Shelves, shelf => shelf.Title == "Audiobook Series");
    }

    [Fact]
    public void ComposeHome_UsesCollectionPreviewImagesWhenGroupArtIsMissing()
    {
        var service = new DiscoveryComposerService(null!);
        var tvGroupId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var movieGroupId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var bookGroupId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var comicGroupId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var audiobookGroupId = Guid.Parse("88888888-8888-8888-8888-888888888888");

        var groups = new[]
        {
            new ContentGroupViewModel
            {
                CollectionId = tvGroupId,
                DisplayName = "The Expanse",
                PrimaryMediaType = "TV",
                WorkCount = 10,
                CoverUrl = "/art/tv-cover.jpg",
                SeasonCount = 2,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            new ContentGroupViewModel
            {
                CollectionId = movieGroupId,
                DisplayName = "Mission: Impossible",
                PrimaryMediaType = "Movies",
                WorkCount = 7,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            new ContentGroupViewModel
            {
                CollectionId = bookGroupId,
                DisplayName = "The Stormlight Archive",
                PrimaryMediaType = "Books",
                WorkCount = 4,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            new ContentGroupViewModel
            {
                CollectionId = comicGroupId,
                DisplayName = "Saga",
                PrimaryMediaType = "Comics",
                WorkCount = 3,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            new ContentGroupViewModel
            {
                CollectionId = audiobookGroupId,
                DisplayName = "Murderbot Diaries",
                PrimaryMediaType = "Audiobooks",
                WorkCount = 6,
                CreatedAt = DateTimeOffset.UtcNow,
            },
        };

        var albumGroups = new[]
        {
            new ContentGroupViewModel
            {
                CollectionId = Guid.Parse("99999999-9999-9999-9999-999999999999"),
                DisplayName = "The Record",
                PrimaryMediaType = "Music",
                WorkCount = 12,
                CoverUrl = "/art/album.jpg",
                Creator = "boygenius",
                Year = "2023",
                CreatedAt = DateTimeOffset.UtcNow,
            }
        };

        var artistGroups = new[]
        {
            new ContentGroupViewModel
            {
                CollectionId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                DisplayName = "boygenius",
                PrimaryMediaType = "Music",
                WorkCount = 18,
                ArtistPhotoUrl = "/art/artist.jpg",
                Creator = "boygenius",
                CreatedAt = DateTimeOffset.UtcNow,
            }
        };

        var previewImages = new Dictionary<Guid, IReadOnlyList<string>>
        {
            [movieGroupId] = ["/art/one.jpg", "/art/two.jpg", "/art/three.jpg"]
        };

        var page = service.ComposeHome([], [], groups, previewImages, albumGroups, artistGroups);

        Assert.Empty(page.Hubs);
        Assert.Contains(page.Shelves, shelf => shelf.Title == "TV Series");
        Assert.Contains(page.Shelves, shelf => shelf.Title == "Movie Series");
        Assert.Contains(page.Shelves, shelf => shelf.Title == "Book Series");
        Assert.Contains(page.Shelves, shelf => shelf.Title == "Comic Series");
        Assert.Contains(page.Shelves, shelf => shelf.Title == "Albums");
        Assert.Contains(page.Shelves, shelf => shelf.Title == "Artists");
        Assert.Contains(page.Shelves, shelf => shelf.Title == "Audiobook Series");
        Assert.DoesNotContain(page.Shelves, shelf => shelf.Title == "Collections built from your library");

        var tvCard = Assert.Single(page.Shelves.Single(shelf => shelf.Title == "TV Series").Items);
        Assert.Equal(DiscoveryCardPresentation.TvSeries, tvCard.Presentation);
        Assert.Equal("/art/tv-cover.jpg", tvCard.CoverUrl);
        Assert.Equal($"/watch/tv/show/{tvGroupId}", tvCard.NavigationUrl);
        Assert.Equal($"/watch/tv/show/{tvGroupId}", tvCard.PrimaryNavigationUrl);

        var movieCard = Assert.Single(page.Shelves.Single(shelf => shelf.Title == "Movie Series").Items);
        Assert.Equal(DiscoveryCardPresentation.MovieSeries, movieCard.Presentation);
        Assert.Equal(3, movieCard.PreviewImages.Count);
        Assert.Equal("/art/one.jpg", movieCard.PreviewImages[0]);

        var albumCard = Assert.Single(page.Shelves.Single(shelf => shelf.Title == "Albums").Items);
        Assert.Equal(DiscoveryCardPresentation.Album, albumCard.Presentation);
        Assert.Equal(DiscoveryCardShape.Square, albumCard.Shape);
        Assert.Contains("/listen/music?", albumCard.NavigationUrl, StringComparison.Ordinal);
        Assert.Contains("groupField=album", albumCard.NavigationUrl, StringComparison.Ordinal);
        Assert.Equal(albumCard.NavigationUrl, albumCard.PrimaryNavigationUrl);

        var artistCard = Assert.Single(page.Shelves.Single(shelf => shelf.Title == "Artists").Items);
        Assert.Equal(DiscoveryCardPresentation.Artist, artistCard.Presentation);
        Assert.Equal("/art/artist.jpg", artistCard.CoverUrl);
        Assert.Equal("/art/artist.jpg", artistCard.TileImageUrl);
        Assert.Equal(DiscoverySurfaceKind.ArtistPhotoSquare, artistCard.SurfaceKind);
        Assert.Equal("/listen/music/artists/boygenius", artistCard.NavigationUrl);

        var audiobookCard = Assert.Single(page.Shelves.Single(shelf => shelf.Title == "Audiobook Series").Items);
        Assert.Equal(DiscoveryCardPresentation.AudiobookSeries, audiobookCard.Presentation);
        Assert.Equal(DiscoveryCardShape.Square, audiobookCard.Shape);
    }

    private static WorkViewModel CreateWork(
        Guid id,
        string mediaType,
        string title,
        string creator,
        string year,
        Guid? collectionId = null,
        DateTimeOffset? createdAt = null,
        string? coverUrl = null,
        string? backgroundUrl = null,
        string? bannerUrl = null,
        string? logoUrl = null,
        IReadOnlyDictionary<string, string>? canonicalExtras = null)
    {
        var canonicalValues = new List<CanonicalValueViewModel>
        {
            CreateCanonical("title", title),
            CreateCanonical("author", creator),
            CreateCanonical("year", year),
            CreateCanonical("release_year", year),
        };

        if (!string.IsNullOrWhiteSpace(coverUrl))
            canonicalValues.Add(CreateCanonical("cover", coverUrl));

        if (!string.IsNullOrWhiteSpace(backgroundUrl))
            canonicalValues.Add(CreateCanonical("background", backgroundUrl));

        if (!string.IsNullOrWhiteSpace(bannerUrl))
            canonicalValues.Add(CreateCanonical("banner", bannerUrl));

        if (!string.IsNullOrWhiteSpace(logoUrl))
            canonicalValues.Add(CreateCanonical("logo", logoUrl));

        if (canonicalExtras is not null)
        {
            foreach (var entry in canonicalExtras)
                canonicalValues.Add(CreateCanonical(entry.Key, entry.Value));
        }

        return new WorkViewModel
        {
            Id = id,
            CollectionId = collectionId,
            MediaType = mediaType,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            CanonicalValues = canonicalValues,
            ResolvedCoverUrl = coverUrl,
            ResolvedBackgroundUrl = backgroundUrl,
            ResolvedBannerUrl = bannerUrl,
            ResolvedLogoUrl = logoUrl,
        };
    }

    private static JourneyItemViewModel CreateJourneyItem(
        Guid workId,
        Guid assetId,
        string mediaType,
        string title,
        string? author,
        DateTimeOffset lastAccessed,
        Guid? collectionId = null,
        string? collectionDisplayName = null,
        string? coverUrl = null,
        string? backgroundUrl = null,
        string? bannerUrl = null,
        string? heroUrl = null,
        string? logoUrl = null,
        IReadOnlyDictionary<string, string>? extendedProperties = null) =>
        new()
        {
            WorkId = workId,
            AssetId = assetId,
            MediaType = mediaType,
            Title = title,
            Author = author,
            CollectionId = collectionId,
            CollectionDisplayName = collectionDisplayName,
            LastAccessed = lastAccessed,
            CoverUrl = coverUrl,
            BackgroundUrl = backgroundUrl,
            BannerUrl = bannerUrl,
            HeroUrl = heroUrl,
            LogoUrl = logoUrl,
            ProgressPct = 42,
            ExtendedProperties = extendedProperties?.ToDictionary(pair => pair.Key, pair => pair.Value) ?? [],
        };

    private static CanonicalValueViewModel CreateCanonical(string key, string value) =>
        new()
        {
            Key = key,
            Value = value,
            LastScoredAt = DateTimeOffset.UtcNow,
        };
}
