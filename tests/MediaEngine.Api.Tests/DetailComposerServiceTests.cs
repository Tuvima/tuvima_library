using MediaEngine.Api.Services.Details;
using MediaEngine.Contracts.Details;

namespace MediaEngine.Api.Tests;

public sealed class DetailComposerServiceTests
{
    [Fact]
    public void TryParseEntityType_DoesNotExposePodcastTypes()
    {
        Assert.False(DetailComposerService.TryParseEntityType("podcast", out _));
        Assert.False(DetailComposerService.TryParseEntityType("podcast-episode", out _));
    }

    [Fact]
    public void TryParseEntityType_ParsesSupportedKebabCaseTypes()
    {
        Assert.True(DetailComposerService.TryParseEntityType("tv-show", out var entityType));
        Assert.Equal(DetailEntityType.TvShow, entityType);
    }

    [Theory]
    [InlineData("listen", DetailPresentationContext.Listen)]
    [InlineData("watch", DetailPresentationContext.Watch)]
    [InlineData("read", DetailPresentationContext.Read)]
    [InlineData("unknown", DetailPresentationContext.Default)]
    public void ParseContext_UsesDefaultForUnknownValues(string value, DetailPresentationContext expected)
    {
        Assert.Equal(expected, DetailComposerService.ParseContext(value));
    }

    [Fact]
    public void ResolveArtworkPresentationMode_PrioritizesRealBackdrops()
    {
        var mode = DetailComposerService.ResolveArtworkPresentationMode(
            DetailEntityType.Movie,
            backdropUrl: "/backdrop.jpg",
            bannerUrl: null,
            coverUrl: "/cover.jpg",
            posterUrl: null,
            portraitUrl: null,
            relatedArtworkCount: 0,
            ownedFormatCount: 1);

        Assert.Equal(ArtworkPresentationMode.CinematicBackdrop, mode);
    }

    [Fact]
    public void ResolveArtworkPresentationMode_UsesCoverGradientWithoutBackdrop()
    {
        var mode = DetailComposerService.ResolveArtworkPresentationMode(
            DetailEntityType.Book,
            backdropUrl: null,
            bannerUrl: null,
            coverUrl: "/cover.jpg",
            posterUrl: null,
            portraitUrl: null,
            relatedArtworkCount: 0,
            ownedFormatCount: 1);

        Assert.Equal(ArtworkPresentationMode.ColorGradientFromArtwork, mode);
    }

    [Fact]
    public void ResolveArtworkPresentationMode_UsesPairedEditionGradientForMultiFormatWorks()
    {
        var mode = DetailComposerService.ResolveArtworkPresentationMode(
            DetailEntityType.Work,
            backdropUrl: null,
            bannerUrl: null,
            coverUrl: "/ebook.jpg",
            posterUrl: null,
            portraitUrl: null,
            relatedArtworkCount: 0,
            ownedFormatCount: 2);

        Assert.Equal(ArtworkPresentationMode.PairedEditionGradient, mode);
    }

    [Fact]
    public void ResolveArtworkPresentationMode_UsesPortraitEchoForPeople()
    {
        var mode = DetailComposerService.ResolveArtworkPresentationMode(
            DetailEntityType.Person,
            backdropUrl: null,
            bannerUrl: null,
            coverUrl: null,
            posterUrl: null,
            portraitUrl: "/portrait.jpg",
            relatedArtworkCount: 0,
            ownedFormatCount: 0);

        Assert.Equal(ArtworkPresentationMode.PortraitEcho, mode);
    }

    [Fact]
    public void ResolveHeroArtwork_PrioritizesBackgroundOverCover()
    {
        var artwork = HeroArtworkResolver.Resolve(
            DetailEntityType.Movie,
            backdropUrl: "/backdrop.jpg",
            bannerUrl: null,
            coverUrl: "/cover.jpg",
            posterUrl: null,
            portraitUrl: null,
            characterImageUrl: null,
            relatedArtworkUrls: []);

        Assert.Equal(HeroArtworkMode.Background, artwork.Mode);
        Assert.True(artwork.HasImage);
        Assert.Equal("/backdrop.jpg", artwork.Url);
    }

    [Fact]
    public void ResolveHeroArtwork_UsesCoverFallbackWithoutBackground()
    {
        var artwork = HeroArtworkResolver.Resolve(
            DetailEntityType.Book,
            backdropUrl: null,
            bannerUrl: null,
            coverUrl: "/cover.jpg",
            posterUrl: null,
            portraitUrl: null,
            characterImageUrl: null,
            relatedArtworkUrls: []);

        Assert.Equal(HeroArtworkMode.CoverFallback, artwork.Mode);
        Assert.True(artwork.HasImage);
        Assert.Equal("/cover.jpg", artwork.Url);
    }

    [Fact]
    public void ResolveHeroArtwork_UsesPlaceholderWithoutImages()
    {
        var artwork = HeroArtworkResolver.Resolve(
            DetailEntityType.Collection,
            backdropUrl: null,
            bannerUrl: null,
            coverUrl: null,
            posterUrl: null,
            portraitUrl: null,
            characterImageUrl: null,
            relatedArtworkUrls: []);

        Assert.Equal(HeroArtworkMode.Placeholder, artwork.Mode);
        Assert.False(artwork.HasImage);
        Assert.Null(artwork.Url);
    }

    [Fact]
    public void DetailComposer_SourceKeepsMovieTabsCastOnlyAndAddsOverflowMenu()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Api/Services/Details/DetailComposerService.cs"));

        Assert.Contains("DetailEntityType.Movie when hasSeries => [\"series\", \"overview\", \"people\", \"universe\", \"related\", \"details\"]", source);
        Assert.Contains("\"people\" => \"Cast\"", source);
        Assert.Contains("sync-settings", source);
    }

    [Fact]
    public void DetailComposer_MapsTvNetworkIntoHeroBrand()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Api/Services/Details/DetailComposerService.cs"));

        Assert.Contains("HeroBrand = BuildHeroBrand", source);
        Assert.Contains("DetailEntityType.TvShow or DetailEntityType.TvSeason or DetailEntityType.TvEpisode", source);
        Assert.Contains("network_logo_url", source);
        Assert.Contains("HeroBrandImageUrl", source);
    }

    [Fact]
    public void DetailComposer_UsesChildArtworkFallbackForCollectionDetails()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Api/Services/Details/DetailComposerService.cs"));

        Assert.Contains("fallbackBackdrop", source);
        Assert.Contains("fallbackCover", source);
        Assert.Contains("collectionBackdrop = FirstNonBlank", source);
        Assert.Contains("collectionCover = FirstNonBlank", source);
        Assert.Contains("'hero_url', 'hero'", source);
        Assert.Contains("SelectMany(w => new[] { w.BackgroundUrl, w.ArtworkUrl })", source);
        Assert.Contains("NULLIF(cover_asset.value, '')", source);
        Assert.Contains("COALESCE(gp.id, p.id, w.id)", source);
        Assert.Contains("ResolveCollectionArtworkUrl", source);
        Assert.Contains("DisplayArtworkUrlResolver.Resolve(value, assetId, kind, state)", source);
    }

    [Fact]
    public void DetailComposer_PopulatesCastCharactersAndRelationshipsForCollectionSurfaces()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Api/Services/Details/DetailComposerService.cs"));
        var creditSource = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Api/Endpoints/PersonCreditQueries.cs"));

        Assert.Contains("BuildCollectionCreditsAsync(collectionId, works, entityType, ct)", source);
        Assert.Contains("BuildCollectionCharactersAsync(collectionId, row.WikidataQid, ct)", source);
        Assert.Contains("BuildUniverseCastGroupsAsync(row.WikidataQid, ct)", source);
        Assert.Contains("BuildUniverseRelationshipGroupsAsync(row.WikidataQid, ct)", source);
        Assert.Contains("ApiImageUrls.BuildCharacterPortraitUrl(row.PortraitId", source);
        Assert.Contains("private sealed class CollectionCharacterRow", source);
        Assert.Contains("private sealed class UniversePerformerRow", source);
        Assert.Contains("DetailEntityType.Movie or DetailEntityType.TvShow or DetailEntityType.TvSeason or DetailEntityType.TvEpisode or DetailEntityType.Universe => [CreditGroupType.Cast]", source);
        Assert.Contains("root.wikidata_qid AS RootWorkQid", creditSource);
        Assert.Contains("await BuildExplicitCastAsync(work.RootWorkQid, db, ct)", creditSource);
        Assert.Contains("BuildFallbackCreditsFromCanonicalArrayAsync(work.RootWorkId.Value", creditSource);
    }

    [Fact]
    public void DetailComposer_SeriesPlacementUsesWikidataPositionsWithoutRowOrderFallback()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Api/Services/Details/DetailComposerService.cs"));

        Assert.Contains("claim_key = 'series_position' AND provider_id = @wikidataProviderId", source);
        Assert.Contains("WellKnownProviders.Wikidata.ToString()", source);
        Assert.Contains("PositionNumber = positionNumber", source);
        Assert.DoesNotContain("PositionNumber = positionNumber ?? index + 1", source);
    }

    [Fact]
    public void DetailComposer_FormatsRatingsAndUsesCreditImageFallbacks()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Api/Services/Details/DetailComposerService.cs"));

        Assert.Contains("FormatRating(detail.Rating)", source);
        Assert.Contains("ToString(\"0.0\"", source);
        Assert.Contains("canonicalArrayKey}_qid", source);
        Assert.Contains("headshot_url", source);
    }

    [Fact]
    public void PersonEndpoints_FallsBackToRemotePortraitsWhenCacheMisses()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Api/Endpoints/PersonEndpoints.cs"));

        Assert.Contains("IsLikelyImageFile", source);
        Assert.Contains("IsLikelyImageBytes", source);
        Assert.Contains("InferImageExtension(person.HeadshotUrl, contentType)", source);
        Assert.Contains("Results.Redirect(remoteUri.ToString())", source);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MediaEngine.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
