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
