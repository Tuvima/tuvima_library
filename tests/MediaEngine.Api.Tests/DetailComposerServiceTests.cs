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

        Assert.Equal(HeroArtworkMode.BackdropWithRenderedTitle, artwork.Mode);
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

        Assert.Equal(HeroArtworkMode.ArtworkFallback, artwork.Mode);
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
    public void DetailComposer_SourceKeepsMovieTabsOnCombinedOverviewAndAddsOverflowMenu()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Api/Services/Details/DetailComposerService.cs"));

        Assert.Contains("DetailEntityType.Movie when hasSeries => [\"overview\", \"cast\", \"universe\", \"related\", \"details\"]", source);
        Assert.Contains("DetailEntityType.Movie => [\"overview\", \"cast\", \"universe\", \"related\", \"details\"]", source);
        Assert.DoesNotContain("DetailEntityType.Movie => [\"overview\", \"people\"", source);
        Assert.Contains("DetailEntityType.TvShow => [\"episodes\", \"overview\", \"cast\", \"universe\", \"details\"]", source);
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
    public void DetailComposer_DrivesHeroProgressFromPlaybackState()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Api/Services/Details/DetailComposerService.cs"));
        var contracts = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Contracts/Details/DetailDtos.cs"));

        Assert.Contains("Progress = heroProgress", source);
        Assert.Contains("BuildHeroProgress", source);
        Assert.Contains("BuildCollectionHeroProgress", source);
        Assert.Contains("LEFT JOIN user_states us ON us.asset_id = ma.id", source);
        Assert.Contains("Label = heroProgress is null ? \"Watch\" : \"Continue Watching\"", source);
        Assert.Contains("Continue watching", source);
        Assert.Contains("public ProgressViewModel? Progress { get; init; }", contracts);
    }

    [Fact]
    public void DetailComposer_BuildsWatchHeroActionsInMockupOrder()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Api/Services/Details/DetailComposerService.cs"));

        Assert.Contains("if (IsWatchEntity(entityType))", source);
        Assert.Contains("Key = \"watch-party\"", source);
        Assert.Contains("Tooltip = \"Watch Party setup is coming soon\"", source);
        Assert.Contains("IsStub = true", source);
        Assert.Contains("Label = \"Watchlist\"", source);
        Assert.Contains("return actions;", source);
        Assert.DoesNotContain("&& SupportsWatchParty(entityType)", source);
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
    public void DetailComposer_PrefersOwnedFormatArtworkForWorkHero()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Api/Services/Details/DetailComposerService.cs"));

        Assert.Contains("var foregroundArtworkUrl = FirstNonBlank(", source);
        Assert.Contains("ownedCoverUrls.FirstOrDefault(),", source);
        Assert.Contains("detail.CoverUrl,", source);
        Assert.Contains("WHERE entity_id = AssetId AND key IN ('cover_url', 'cover', 'poster_url', 'poster')", source);
        Assert.Contains("WHERE entity_id = WorkId AND key IN ('cover_url', 'cover', 'poster_url', 'poster')", source);
        Assert.Contains("WHERE entity_id = RootWorkId AND key IN ('cover_url', 'cover', 'poster_url', 'poster')", source);
    }

    [Fact]
    public void DetailComposer_PopulatesCastCharactersAndRelationshipsForCollectionSurfaces()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Api/Services/Details/DetailComposerService.cs"));
        var creditSource = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Api/Endpoints/PersonCreditQueries.cs"));

        Assert.Contains("BuildCollectionCreditsAsync(collectionId, rootWorkId, works, entityType, values, ct)", source);
        Assert.Contains("BuildCollectionCharactersAsync(collectionId, row.WikidataQid, ct)", source);
        Assert.Contains("BuildUniverseCastGroupsAsync(row.WikidataQid, ct)", source);
        Assert.Contains("BuildUniverseRelationshipGroupsAsync(row.WikidataQid, ct)", source);
        Assert.Contains("ApiImageUrls.BuildCharacterPortraitUrl(row.PortraitId", source);
        Assert.Contains("private sealed class CollectionCharacterRow", source);
        Assert.Contains("private sealed class UniversePerformerRow", source);
        Assert.Contains("DetailEntityType.Movie => directors.Take(1).Concat(cast.Take(5)).ToList()", source);
        Assert.Contains("DetailEntityType.TvShow or DetailEntityType.TvSeason or DetailEntityType.TvEpisode => cast.Take(5).ToList()", source);
        Assert.Contains("Title = \"Primary Cast\"", source);
        Assert.Contains(") AS RootWorkQid", creditSource);
        Assert.Contains("await BuildExplicitCastAsync(work.RootWorkQid, rootRankMap, db, ct)", creditSource);
        Assert.Contains("BuildFallbackCreditsFromCanonicalArrayAsync(work.RootWorkId.Value, canonicalArrayRepo, personRepo, ct)", creditSource);
        Assert.Contains("CastRankMap.BuildAsync", creditSource);
        Assert.Contains("ORDER BY cpl.rowid", creditSource);
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
    public void DetailComposer_SeriesPlacementUsesKnownSeriesMembersForMissingSlots()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Api/Services/Details/DetailComposerService.cs"));

        Assert.Contains("series_qid", source);
        Assert.Contains("GetItemsBySeriesQidAsync(seriesQid, ct)", source);
        Assert.Contains("MergeManifestItems(items, scopedManifestItems, currentWorkQid, currentWorkId, entityType)", source);
        Assert.Contains("FROM series_members", source);
        Assert.Contains("MergeSeriesManifestPlaceholdersAsync(items, seriesQid, detail.WikidataQid, workId, entityType, ct)", source);
        Assert.Contains("TotalKnownItems = items.Count", source);
        Assert.Contains("Missing from library", source);
    }

    [Fact]
    public void DetailComposer_SeriesPlacementDoesNotUseFranchiseAsSeriesSource()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Api/Services/Details/DetailComposerService.cs"));
        var hydratorSource = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Providers/Services/WikidataSeriesManifestHydrationService.cs"));

        Assert.Contains("ResolveSeriesPlacementOptions(detail, entityType)", source);
        Assert.Contains("current_work.collection_id AS CollectionId", source);
        Assert.Contains("w.collection_id = current.CollectionId", source);
        Assert.DoesNotContain("key IN ('series', 'franchise')", source);
        Assert.DoesNotContain("GetDetailCanonicalValue(detail, MetadataFieldConstants.Franchise)", source);
        Assert.DoesNotContain("qid = GetDetailCanonicalValue(detail, \"franchise_qid\")", source);
        Assert.Contains("IsManifestItemInMediaScope", source);
        Assert.Contains("MediaType.Movies", hydratorSource);
        Assert.Contains("MediaType.Comics", hydratorSource);
        Assert.DoesNotContain("\"franchise_qid\"", hydratorSource);
    }

    [Fact]
    public void DetailComposer_FormatsRatingsAndUsesCreditImageFallbacks()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Api/Services/Details/DetailComposerService.cs"));

        Assert.Contains("FormatRating(detail.Rating)", source);
        Assert.Contains("ToString(\"0.0\"", source);
        Assert.Contains("canonicalArrayKey + MetadataFieldConstants.CompanionQidSuffix", source);
        Assert.Contains("headshot_url", source);
    }

    [Fact]
    public void DetailComposer_UsesRootShowTitlesAndClaimFallbacksForPeople()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Api/Services/Details/DetailComposerService.cs"));
        var creditSource = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Api/Endpoints/PersonCreditQueries.cs"));
        var scoringSource = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Providers/Services/ScoringHelper.cs"));
        var retailWorker = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Providers/Workers/RetailMatchWorker.cs"));
        var bridgeWorker = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Providers/Workers/WikidataBridgeWorker.cs"));

        Assert.Contains("ResolveCollectionTitle(entityType, row.DisplayName, rootValues, values)", source);
        Assert.Contains("GetValue(rootValues, MetadataFieldConstants.Title)", source);
        Assert.Contains("StripUniverseSuffix(displayName)", source);
        Assert.Contains("LoadContributorEntriesFromClaimsAsync", source);
        Assert.Contains("CreditGroupType.PrimaryArtists", source);
        Assert.Contains("CreditGroupType.MusicCredits", source);
        Assert.Contains("CreditGroupType.Illustrators", source);
        Assert.Contains("BuildPersonCreditEntityId(credit.PersonId, credit.WikidataQid, credit.Name)", source);

        Assert.Contains("BuildFallbackCreditsFromMetadataClaimsAsync", creditSource);
        Assert.Contains("mc.claim_key IN ('cast_member', 'cast_member_qid')", creditSource);
        Assert.Contains("ExtractQid(entry.ValueQid)", creditSource);

        Assert.Contains("BuildCanonicalArrayEntries(winningClaims, qidClaims)", scoringSource);
        Assert.Contains("ValueQid = parsed.Qid", scoringSource);
        Assert.Contains("arrayRepo: _arrayRepo", retailWorker);
        Assert.Contains("arrayRepo: _arrayRepo", bridgeWorker);
    }

    [Fact]
    public void PersonEndpoints_DownloadsRemotePortraitsButDoesNotRedirectToRemoteImages()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Api/Endpoints/PersonEndpoints.cs"));

        Assert.Contains("IsLikelyImageFile", source);
        Assert.Contains("IsLikelyImageBytes", source);
        Assert.Contains("InferImageExtension(person.HeadshotUrl, contentType)", source);
        Assert.Contains("return Results.File(bytes, contentType ?? GetImageMimeType(localPath), Path.GetFileName(localPath))", source);
        Assert.DoesNotContain("Results.Redirect(remoteUri.ToString())", source);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MediaEngine.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
