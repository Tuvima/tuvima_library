using MediaEngine.Contracts.Display;
using MediaEngine.Domain;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.MediaTiles;

namespace MediaEngine.Web.Tests;

public sealed class MediaTileComposerServiceTests
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
                CoverSmallUrl: "http://localhost:61495/stream/artwork/11111111-1111-1111-1111-111111111111?size=s",
                CoverMediumUrl: "http://localhost:61495/stream/artwork/11111111-1111-1111-1111-111111111111?size=m",
                CoverLargeUrl: "http://localhost:61495/stream/artwork/11111111-1111-1111-1111-111111111111?size=l",
                SquareUrl: null,
                SquareSmallUrl: null,
                SquareMediumUrl: null,
                SquareLargeUrl: null,
                BannerUrl: null,
                BannerSmallUrl: null,
                BannerMediumUrl: null,
                BannerLargeUrl: null,
                BackgroundUrl: null,
                BackgroundSmallUrl: null,
                BackgroundMediumUrl: null,
                BackgroundLargeUrl: null,
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

        var mapped = MediaTileComposerService.FromDisplayCard(card);

        Assert.Equal(MediaTileTextMode.CoverOnly, mapped.TileTextMode);
        Assert.Equal(MediaTilePreviewPlacement.Bottom, mapped.PreviewPlacement);
        Assert.Equal(["Frank Herbert", "Science Fiction"], mapped.HoverFacts);
        Assert.Equal(32, mapped.ProgressPct);
        Assert.Equal("http://localhost:61495/stream/artwork/11111111-1111-1111-1111-111111111111?size=s", mapped.TileImageUrl);
        Assert.Equal("http://localhost:61495/stream/artwork/11111111-1111-1111-1111-111111111111?size=s 320w, http://localhost:61495/stream/artwork/11111111-1111-1111-1111-111111111111?size=m 960w", mapped.TileImageSrcSet);
        Assert.Equal("http://localhost:61495/stream/artwork/11111111-1111-1111-1111-111111111111?size=m", mapped.HoverImageUrl);
        Assert.Equal($"/read/{assetId}", mapped.PrimaryNavigationUrl);
        Assert.Equal("Continue Reading", mapped.PrimaryActionLabel);
    }

    [Fact]
    public void FromDisplayCard_MapsWatchBadgesToTileFieldsAndLogos()
    {
        var workId = Guid.Parse("44444444-1111-1111-1111-444444444444");
        var action = new DisplayActionDto("playAsset", "Play", WorkId: workId, WebUrl: $"/watch/movie/{workId}");
        var card = new DisplayCardDto(
            Id: workId,
            WorkId: workId,
            AssetId: null,
            CollectionId: null,
            MediaType: "Movie",
            GroupingType: "work",
            Title: "Arrival",
            Subtitle: "2016",
            Facts: ["2016", "Science Fiction"],
            Artwork: EmptyArtwork("#60A5FA"),
            PreferredShape: "landscape",
            Presentation: "movie",
            TileTextMode: "caption",
            PreviewPlacement: "smart",
            Progress: null,
            Actions: [action],
            Flags: new DisplayCardFlagsDto(true, false, true, false, false),
            SortTimestamp: DateTimeOffset.Parse("2026-04-24T12:00:00Z"))
        {
            Badges = [new DisplayCardBadgeDto("quality", "4K"), new DisplayCardBadgeDto("source", "HBO")],
        };

        var mapped = MediaTileComposerService.FromDisplayCard(card);

        Assert.Equal("4K", mapped.QualityBadge);
        Assert.Equal("HBO", mapped.SourceBadgeLabel);
        Assert.Contains("max", mapped.SourceLogoUrl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromDisplayCard_MapsCollectionArtworkIntoBackdropAndPreviewImages()
    {
        var collectionId = Guid.Parse("66666666-1111-1111-1111-666666666666");
        var action = new DisplayActionDto("openCollection", "Explore", CollectionId: collectionId, WebUrl: $"/collection/{collectionId}");
        var card = new DisplayCardDto(
            Id: collectionId,
            WorkId: null,
            AssetId: null,
            CollectionId: collectionId,
            MediaType: "Movie",
            GroupingType: "movieSeries",
            Title: "Sci-Fi Favorites",
            Subtitle: "12 titles",
            Facts: ["12 titles"],
            Artwork: new DisplayArtworkDto(
                CoverUrl: "/cover.jpg",
                CoverSmallUrl: "/cover-s.jpg",
                CoverMediumUrl: "/cover-m.jpg",
                CoverLargeUrl: "/cover-l.jpg",
                SquareUrl: null,
                SquareSmallUrl: null,
                SquareMediumUrl: null,
                SquareLargeUrl: null,
                BannerUrl: null,
                BannerSmallUrl: null,
                BannerMediumUrl: null,
                BannerLargeUrl: null,
                BackgroundUrl: "/background.jpg",
                BackgroundSmallUrl: "/background-s.jpg",
                BackgroundMediumUrl: "/background-m.jpg",
                BackgroundLargeUrl: "/background-l.jpg",
                LogoUrl: null,
                CoverWidthPx: 1000,
                CoverHeightPx: 1500,
                SquareWidthPx: null,
                SquareHeightPx: null,
                BannerWidthPx: null,
                BannerHeightPx: null,
                BackgroundWidthPx: 1920,
                BackgroundHeightPx: 1080,
                AccentColor: "#60A5FA"),
            PreferredShape: "landscape",
            Presentation: "movieSeries",
            TileTextMode: "coverOnly",
            PreviewPlacement: "smart",
            Progress: null,
            Actions: [action],
            Flags: new DisplayCardFlagsDto(true, false, false, true, false),
            SortTimestamp: DateTimeOffset.Parse("2026-04-24T12:00:00Z"));

        var mapped = MediaTileComposerService.FromDisplayCard(card);

        Assert.True(mapped.IsCollection);
        Assert.Equal(MediaTileShape.Landscape, mapped.Shape);
        Assert.Equal(MediaTileSurfaceKind.BannerLandscape, mapped.SurfaceKind);
        Assert.Equal("/background-s.jpg", mapped.TileImageUrl);
        Assert.Contains("/cover-s.jpg", mapped.PreviewImages);
        Assert.Contains("/background-s.jpg", mapped.PreviewImages);
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
            Artwork: new DisplayArtworkDto(
                CoverUrl: "/cover.jpg",
                CoverSmallUrl: "/cover-s.jpg",
                CoverMediumUrl: "/cover-m.jpg",
                CoverLargeUrl: "/cover-l.jpg",
                SquareUrl: null,
                SquareSmallUrl: null,
                SquareMediumUrl: null,
                SquareLargeUrl: null,
                BannerUrl: null,
                BannerSmallUrl: null,
                BannerMediumUrl: null,
                BannerLargeUrl: null,
                BackgroundUrl: "/background.jpg",
                BackgroundSmallUrl: "/background-s.jpg",
                BackgroundMediumUrl: "/background-m.jpg",
                BackgroundLargeUrl: "/background-l.jpg",
                LogoUrl: null,
                CoverWidthPx: null,
                CoverHeightPx: null,
                SquareWidthPx: null,
                SquareHeightPx: null,
                BannerWidthPx: null,
                BannerHeightPx: null,
                BackgroundWidthPx: null,
                BackgroundHeightPx: null,
                AccentColor: "#60A5FA"),
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
            Hero: new DisplayHeroDto("Arrival", null, "Featured from your library", card.Artwork, null, [action])
            {
                Facts = card.Facts,
            },
            Shelves: [new DisplayShelfDto("movies", "Movies in your library", null, [card], null)],
            Catalog: [card]);

        var mapped = MediaTileComposerService.FromDisplayPage(page);

        Assert.Equal("watch", mapped.Key);
        Assert.Equal("Arrival", mapped.Hero?.Title);
        Assert.Single(mapped.Shelves);
        Assert.Single(mapped.Catalog);
        Assert.Equal(["2016", "Science Fiction"], mapped.Hero?.MetaPills);
        Assert.Equal(["Arrival"], mapped.Spotlights.Select(slide => slide.Title));
        Assert.Equal(["2016", "Science Fiction"], mapped.Catalog[0].HoverFacts);
        Assert.Equal("/background-s.jpg", mapped.Catalog[0].TileImageUrl);
        Assert.Equal("/background-m.jpg", mapped.Catalog[0].HoverImageUrl);
    }

    [Fact]
    public void FromDisplayPage_DerivesUpToFiveSpotlightsFromContinueShelf()
    {
        var cards = Enumerable.Range(1, 6)
            .Select(index =>
            {
                var workId = Guid.Parse($"55555555-0000-0000-0000-{index:000000000000}");
                var action = new DisplayActionDto("playAsset", "Play", WorkId: workId, WebUrl: $"/watch/movie/{workId}");
                return new DisplayCardDto(
                    Id: workId,
                    WorkId: workId,
                    AssetId: null,
                    CollectionId: null,
                    MediaType: "Movie",
                    GroupingType: "work",
                    Title: $"Movie {index}",
                    Subtitle: "2016",
                    Facts: [$"Fact {index}"],
                    Artwork: EmptyArtwork("#60A5FA"),
                    PreferredShape: "landscape",
                    Presentation: "movie",
                    TileTextMode: "caption",
                    PreviewPlacement: "smart",
                    Progress: null,
                    Actions: [action],
                    Flags: new DisplayCardFlagsDto(true, false, true, false, false),
                    SortTimestamp: DateTimeOffset.Parse("2026-04-24T12:00:00Z"));
            })
            .ToList();
        var page = new DisplayPageDto(
            Key: "home",
            Title: "Home",
            Subtitle: null,
            Hero: new DisplayHeroDto(cards[0].Title, cards[0].Subtitle, "Jump Back In", cards[0].Artwork, null, cards[0].Actions)
            {
                Facts = cards[0].Facts,
            },
            Shelves: [new DisplayShelfDto("continue", "Jump Back In", null, cards, null)],
            Catalog: cards);

        var mapped = MediaTileComposerService.FromDisplayPage(page);

        Assert.Equal(5, mapped.Spotlights.Count);
        Assert.Equal(["Movie 1", "Movie 2", "Movie 3", "Movie 4", "Movie 5"], mapped.Spotlights.Select(slide => slide.Title));
        Assert.Equal(["Fact 2"], mapped.Spotlights[1].MetaPills);
    }

    private static DisplayArtworkDto EmptyArtwork(string? accentColor) =>
        new(
            CoverUrl: null,
            CoverSmallUrl: null,
            CoverMediumUrl: null,
            CoverLargeUrl: null,
            SquareUrl: null,
            SquareSmallUrl: null,
            SquareMediumUrl: null,
            SquareLargeUrl: null,
            BannerUrl: null,
            BannerSmallUrl: null,
            BannerMediumUrl: null,
            BannerLargeUrl: null,
            BackgroundUrl: null,
            BackgroundSmallUrl: null,
            BackgroundMediumUrl: null,
            BackgroundLargeUrl: null,
            LogoUrl: null,
            CoverWidthPx: null,
            CoverHeightPx: null,
            SquareWidthPx: null,
            SquareHeightPx: null,
            BannerWidthPx: null,
            BannerHeightPx: null,
            BackgroundWidthPx: null,
            BackgroundHeightPx: null,
            AccentColor: accentColor);

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
