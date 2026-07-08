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
    public void DetailComposer_SourceKeepsMovieTabsOnCombinedOverviewAndUsesDirectEdit()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Api/Services/Details/DetailComposerService.cs"));

        Assert.Contains("DetailEntityType.Movie when hasUniverse => [\"overview\", \"cast\", \"universe\", \"details\"]", source);
        Assert.Contains("DetailEntityType.Movie => [\"overview\", \"cast\", \"details\"]", source);
        Assert.DoesNotContain("DetailEntityType.Movie => [\"overview\", \"cast\", \"universe\", \"related\", \"details\"]", source);
        Assert.DoesNotContain("DetailEntityType.Movie => [\"overview\", \"people\"", source);
        Assert.Contains("DetailEntityType.TvShow => hasUniverse ? [\"episodes\", \"overview\", \"cast\", \"universe\", \"details\"] : [\"episodes\", \"overview\", \"cast\", \"details\"]", source);
        Assert.Contains("DetailEntityType.TvSeason => [\"episodes\", \"overview\", \"cast\", \"details\"]", source);
        Assert.Contains("DetailEntityType.Book when hasUniverse => [\"overview\", \"credits\", \"universe\", \"details\"]", source);
        Assert.Contains("DetailEntityType.Book => [\"overview\", \"credits\", \"details\"]", source);
        Assert.Contains("DetailEntityType.Audiobook when hasUniverse && hasChapters => [\"overview\", \"chapters\", \"credits\", \"universe\", \"details\"]", source);
        Assert.Contains("DetailEntityType.Audiobook when hasChapters => [\"overview\", \"chapters\", \"credits\", \"details\"]", source);
        Assert.Contains("DetailEntityType.Audiobook => [\"overview\", \"credits\", \"details\"]", source);
        Assert.DoesNotContain("DetailEntityType.Book or DetailEntityType.Audiobook => [\"overview\", \"credits\", \"chapters\", \"universe\", \"editions\", \"details\"]", source);
        Assert.Contains("DetailEntityType.ComicIssue when hasUniverse => [\"overview\", \"credits\", \"universe\", \"editions\", \"details\"]", source);
        Assert.Contains("DetailEntityType.MusicTrack => [\"overview\", \"credits\", \"related\", \"details\"]", source);
        Assert.Contains("HasUniverseRelationship(relationships)", source);
        Assert.DoesNotContain("sync-settings", source);
        Assert.Contains("BuildOverflowActions(workId, entityType, isAdminView)", source);
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
    public void DetailComposer_DetailAndChildListsPreferDisplayOverrides()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Api/Services/Details/DetailComposerService.cs"));

        Assert.Contains("LoadWorkDisplayOverridesAsync(workId", source, StringComparison.Ordinal);
        Assert.Contains("ResolveDisplayTitleOverride(displayOverrides, entityType)", source, StringComparison.Ordinal);
        Assert.Contains("w.display_overrides_json AS TEXT) AS WorkDisplayOverridesJson", source, StringComparison.Ordinal);
        Assert.Contains("ResolveDisplayTitleOverride(", source, StringComparison.Ordinal);
        Assert.Contains("DetailEntityType.TvEpisode => [\"episode_title\", \"title\", \"display_title\"]", source, StringComparison.Ordinal);
        Assert.Contains("DetailEntityType.MusicAlbum => [\"album\", \"title\", \"display_title\"]", source, StringComparison.Ordinal);
        Assert.Contains("return $\"{season}:{episode}\";", source, StringComparison.Ordinal);
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
    public void DetailComposer_UsesAudiobookResumeForContinueActionsAndChapterProgress()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Api/Services/Details/DetailComposerService.cs"));

        Assert.Contains("ResolveAudiobookTotalDurationSeconds(row, chapters)", source);
        Assert.Contains("LoadAudiobookResumeAsync(conn, row.WorkId, row.AssetId, manifest?.Resume, totalDurationSeconds, ct)", source);
        Assert.Contains("NormalizeAudiobookResumePosition(fallback, durationSeconds)", source);
        Assert.Contains("durationSeconds.Value * Math.Clamp(progressPct.Value, 0, 100) / 100d", source);
        Assert.Contains("FROM audiobook_listen_active_segments", source);
        Assert.Contains("FROM audiobook_listen_history", source);
        Assert.Contains("IsMeaningfulAudiobookResume", source);
        Assert.Contains("BuildListenHeroProgressLabel", source);
        Assert.Contains("BuildAudiobookHeroProgress(entityType, detail.Runtime, mediaGroups)", source);
        Assert.Contains("current.ResumePositionSeconds.Value / totalSeconds * 100", source);
        Assert.Contains("Label = heroProgress is null ? \"Listen\" : \"Continue\"", source);
        Assert.Contains("ResumePositionSeconds = IsPositionWithinChapter(resumeSeconds, chapter.StartSeconds, chapter.EndSeconds)", source);
        Assert.Contains("ProgressPercent = progressPercent", source);
        Assert.Contains("DurationSeconds = durationSeconds", source);
    }

    [Fact]
    public void DetailComposer_ComputesMediaGroupCompletionFromOwnedAssets()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Api/Services/Details/DetailComposerService.cs"));
        var contracts = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Contracts/Details/DetailDtos.cs"));

        Assert.Contains("CASE WHEN MAX(ma.id) IS NULL THEN 0 ELSE 1 END AS HasAsset", source);
        Assert.Contains("COALESCE(w.is_catalog_only, 0) AS IsCatalogOnly", source);
        Assert.Contains("ApplyMediaGroupCompletion", source);
        Assert.Contains("var expectedTotal = manifest?.ExpectedTotal", source);
        Assert.Contains("BuildCollectionMediaGroups(entityType, displayWorks, favoriteWorkIds, expectedTotal)", source);
        Assert.Contains("var total = Math.Max(group.Items.Count, group.TotalCount)", source);
        Assert.Contains("DetailEntityType.MovieSeries => \"Films\"", source);
        Assert.Contains("DetailEntityType.BookSeries => \"Books\"", source);
        Assert.Contains("InitiallyCollapsed = total > 0 && owned == 0", source);
        Assert.Contains("Actions = work.IsOwned ?", source);
        Assert.Contains("public int OwnedCount { get; init; }", contracts);
        Assert.Contains("public double CompletionPercent { get; init; }", contracts);
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
        Assert.Contains("BuildFavoriteAction", source);
        Assert.Contains("Key = \"favorite\"", source);
        Assert.Contains("Icon = isSelected ? \"favorite_filled\" : \"favorite\"", source);
        Assert.DoesNotContain("BuildReactionAction", source);
        Assert.DoesNotContain("reaction-menu", source);
        Assert.DoesNotContain("Thumbs up", source);
        Assert.DoesNotContain("Thumbs down", source);
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
    public void DetailComposer_JoinsCollectionItemsWhenLoadingCollectionWorks()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Api/Services/Details/DetailComposerService.cs"));

        Assert.Contains("collectionId = GuidSql.ToBlob(collectionId)", source, StringComparison.Ordinal);
        Assert.Contains("rootWorkId = rootWorkId.HasValue ? GuidSql.ToBlob(rootWorkId.Value) : null", source, StringComparison.Ordinal);
        Assert.Contains("defaultOwnerUserId = GuidSql.ToBlob(DefaultOwnerUserId)", source, StringComparison.Ordinal);
        Assert.Contains("GuidSql.FromDb(bytes).ToString(\"D\")", source, StringComparison.Ordinal);
        Assert.Contains("(string?)StringValue(row.WorkDisplayOverridesJson)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("collectionId = collectionId.ToString()", source, StringComparison.Ordinal);
        Assert.Contains("LEFT JOIN collection_items ci ON ci.work_id = w.id AND ci.collection_id = @collectionId", source);
        Assert.Contains("OR ci.collection_id = @collectionId", source);
        Assert.Contains("ORDER BY COALESCE(ci.sort_order, 9999)", source);
        var contributorStart = source.IndexOf("LoadContributorTargetIdsAsync", StringComparison.Ordinal);
        var contributorEnd = source.IndexOf("if (row is null)", contributorStart, StringComparison.Ordinal);
        Assert.DoesNotContain("collection_items ci", source[contributorStart..contributorEnd]);
    }

    [Fact]
    public void MediaEditorNavigator_UsesSettableRowsForSqliteMaterialization()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Api/Services/ReadServices/MediaEditorNavigationReadService.cs"));

        Assert.Contains("private sealed class NavigatorTreeRow", source);
        Assert.Contains("public long? Ordinal { get; init; }", source);
        Assert.Contains("public long IsCatalogOnly { get; init; }", source);
        Assert.Contains("private sealed class NavigatorValueRow", source);
        Assert.Contains("CAST(MIN(ma.id) AS TEXT) AS AssetId", source);
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
        var creditSource = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Api/Services/ReadServices/PersonCreditReadService.cs"));

        Assert.Contains("BuildCollectionCreditsAsync(collectionId, rootWorkId, works, entityType, values, ct)", source);
        Assert.Contains("BuildCollectionCharactersAsync(collectionId, row.WikidataQid, ct)", source);
        Assert.Contains("BuildUniverseCastGroupsAsync(row.WikidataQid, ct)", source);
        Assert.Contains("BuildUniverseRelationshipGroupsAsync(row.WikidataQid, ct)", source);
        Assert.Contains("ApiImageUrls.BuildCharacterPortraitUrl(row.PortraitId", source);
        Assert.Contains("private sealed class CollectionCharacterRow", source);
        Assert.Contains("private sealed class UniversePerformerRow", source);
        Assert.Contains("DetailEntityType.Movie => directors.Take(1).Concat(cast.Take(5)).ToList()", source);
        Assert.Contains("DetailEntityType.TvShow or DetailEntityType.TvSeason or DetailEntityType.TvEpisode => cast.Take(5).ToList()", source);
        Assert.Contains("Title = \"Actors\"", source);
        Assert.Contains(") AS RootWorkQid", creditSource);
        Assert.Contains("await BuildExplicitCastAsync(work.RootWorkQid, rootRankMap, _db, ct)", creditSource);
        Assert.Contains("BuildFallbackCreditsFromCanonicalArrayAsync(work.RootWorkId.Value, _canonicalArrayRepo, _personRepo, ct)", creditSource);
        Assert.Contains("CastRankMap.BuildAsync", creditSource);
        Assert.Contains("ORDER BY cpl.rowid", creditSource);
    }

    [Fact]
    public void DetailComposer_SequencePlacementUsesWikidataPositionsWithoutRowOrderFallback()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Api/Services/Details/DetailComposerService.cs"));

        Assert.Contains("claim_key = 'series_position' AND provider_id = @wikidataProviderId", source);
        Assert.Contains("WellKnownProviders.Wikidata.ToString()", source);
        Assert.Contains("QueryAsync<SequenceRow>", source);
        Assert.Contains("PositionNumber = positionNumber", source);
        Assert.DoesNotContain("PositionNumber = positionNumber ?? index + 1", source);
    }

    [Fact]
    public void DetailComposer_SequencePlacementUsesKnownSeriesMembersForMissingSlots()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Api/Services/Details/DetailComposerService.cs"));

        Assert.Contains("series_qid", source);
        Assert.Contains("LoadManifestItemsForSequenceContainerAsync(normalizedContainerId, ct)", source);
        Assert.Contains("WHERE series_qid = @containerId", source);
        Assert.Contains("GetItemsBySeriesQidAsync(containerId, ct)", source);
        Assert.Contains("WHERE parent_collection_qid = @containerId", source);
        Assert.Contains("var exactManifestItems = manifestItems", source);
        Assert.Contains("SequenceContainerIdEquals(item.SeriesQid, normalizedContainerId)", source);
        Assert.Contains("manifestItems = exactManifestItems", source);
        Assert.Contains("MergeManifestItems(items, scopedManifestItems, currentWorkQid, currentWorkId, entityType)", source);
        Assert.Contains("FROM series_members", source);
        Assert.Contains("MergeSequenceManifestPlaceholdersAsync(items, manifestContainerId, detail.WikidataQid, workId, entityType, ct)", source);
        Assert.Contains("ApplyExactManifestPositionsAsync(items, manifestContainerId, entityType, ct)", source);
        Assert.Contains("SELECT linked_work_id AS LinkedWorkId", source);
        Assert.Contains("WHERE series_qid = @seriesQid", source);
        Assert.Contains("ToManifestPosition", source);
        Assert.Contains("ResolveLinkedManifestSequenceContainerOptionsAsync(workId, entityType, detail.MediaType, ct)", source);
        Assert.Contains("ResolveLocalSequenceContainerOptionAsync(workId, entityType, detail.MediaType, ct)", source);
        Assert.DoesNotContain("smi.parent_collection_qid AS ContainerId", source);
        Assert.Contains("containerKind') AS TEXT), 'OrderedSeries') NOT IN ('Franchise', 'Universe', 'WikimediaList', 'PublisherOrProductionList')", source);
        Assert.Contains("IsParentSequenceContainer(scopedManifestItems, normalizedContainerId)", source);
        Assert.Contains("items.Count <= 1 && !hasExplicitSequenceEvidence", source);
        Assert.Contains("!hasExplicitSequenceEvidence && !hasPositionEvidence", source);
        Assert.Contains("LoadSequenceExpectedTotalAsync(containerId, ct)", source);
        Assert.Contains("?? await LoadSequenceExpectedTotalAsync(sourceContainerId, ct)", source);
        Assert.Contains("TotalKnownItems = totalKnownItems", source);
        Assert.Contains("DeduplicateManifestMergeItems(merged)", source);
        Assert.Contains("BuildOwnedPositionSet(merged)", source);
        Assert.Contains("BuildManifestDisplayPositions(manifestItems)", source);
        Assert.Contains("SequenceEqual(Enumerable.Range(1, sourcePositions.Count))", source);
        Assert.Contains("NormalizeContiguousSequenceDisplayPositions(items, entityType)", source);
        Assert.Contains("ShouldCompactContiguousSequenceDisplayPositions(entityType)", source);
        Assert.Contains("=> entityType is DetailEntityType.Movie or DetailEntityType.MovieSeries", source);
        Assert.Contains("positions.SequenceEqual(Enumerable.Range(min, positions.Count))", source);
        Assert.Contains("BuildManifestMergeKey", source);
        Assert.DoesNotContain("if (isLinkedOwned || (!string.IsNullOrWhiteSpace(manifestItem.ItemQid) && ownedQids.Contains(manifestItem.ItemQid)))", source);
        Assert.Contains("Missing from library", source);
    }

    [Fact]
    public void DetailComposer_DoesNotCompactComicIssueOrdinalsToOwnedSubsetPositions()
    {
        var items = new List<SequenceItemViewModel>
        {
            new() { Title = "Batman", PositionNumber = 405, PositionSort = 405, PositionLabel = "405" },
            new() { Title = "Batman", PositionNumber = 406, PositionSort = 406, PositionLabel = "406" },
            new() { Title = "Batman", PositionNumber = 407, PositionSort = 407, PositionLabel = "407" },
        };

        var normalized = InvokePrivate<List<SequenceItemViewModel>>(
            "NormalizeContiguousSequenceDisplayPositions",
            items,
            DetailEntityType.ComicIssue);

        Assert.Equal([405, 406, 407], normalized.Select(item => item.PositionNumber));
        Assert.Equal([405d, 406d, 407d], normalized.Select(item => item.PositionSort));
        Assert.Equal(["405", "406", "407"], normalized.Select(item => item.PositionLabel));
    }

    [Fact]
    public void DetailComposer_ExposesWikidataAndSeriesSourceLinks()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Api/Services/Details/DetailComposerService.cs"));
        var contracts = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Contracts/Details/DetailDtos.cs"));

        Assert.Contains("SourceLinks = BuildExternalSourceLinks(detail.WikidataQid", source);
        Assert.Contains("\"wikidata-series\"", source);
        Assert.Contains("\"comicvine-issue\"", source);
        Assert.Contains("FirstText(sequence?.SourceContainerId, sequence?.ContainerId)", source);
        Assert.Contains("BuildWikidataEntityUrl(seriesQid)", source);
        Assert.Contains("ResolveComicVineIssueUrl(values)", source);
        Assert.Contains("\"tmdb\"", source);
        Assert.Contains("BuildTmdbSourceUrl(values)", source);
        Assert.Contains("\"apple-music-album\"", source);
        Assert.Contains("BuildAppleMusicAlbumUrl", source);
        Assert.Contains("\"musicbrainz-release-group\"", source);
        Assert.Contains("BuildMusicBrainzUrl(\"release-group\"", source);
        Assert.Contains("MetadataFieldConstants.WikidataQidScope", source);
        Assert.Contains("\"Series on Wikidata\"", source);
        Assert.Contains("\"Series/run identity source\"", source);
        Assert.Contains("public IReadOnlyList<ExternalSourceLinkViewModel> SourceLinks { get; init; } = [];", contracts);
        Assert.Contains("public sealed class ExternalSourceLinkViewModel", contracts);
        Assert.Contains("public string? SourceContainerId { get; init; }", contracts);
        Assert.Contains("public IReadOnlyList<string> EquivalentContainerIds { get; init; } = [];", contracts);
    }

    [Fact]
    public void DetailComposer_UsesIssueScopedComicDescriptionAndAttribution()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Api/Services/Details/DetailComposerService.cs"));

        Assert.Contains("MetadataFieldConstants.IssueDescription", source);
        Assert.Contains("BuildComicIssueFallbackDescription", source);
        Assert.Contains("selection.IsGeneratedFallback", source);
        Assert.Contains("SourceName = \"Comic Vine\"", source);
        Assert.Contains("SourceTitle = \"issue synopsis\"", source);
        Assert.Contains("LicenseName = \"Comic Vine API Terms\"", source);
        Assert.Contains("LicenseUrl = \"https://comicvine.gamespot.com/api/\"", source);
        Assert.Contains("if (!isComicVine)", source);
        Assert.Contains("normalizedTitle == normalizedSeries", source);
        Assert.DoesNotContain("Comic Vine: issue synopsis\";\n            SourceName = \"Wikipedia\"", source);
    }

    [Fact]
    public void DetailComposer_SequencePlacementUsesManifestOptionsAndHydratorRejectsMovieFranchiseFallback()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Api/Services/Details/DetailComposerService.cs"));
        var hydratorSource = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Providers/Services/WikidataSeriesManifestHydrationService.cs"));

        Assert.Contains("ResolveSequenceContainerOptions(detail, entityType)", source);
        Assert.Contains("current_work.collection_id AS CollectionId", source);
        Assert.Contains("w.collection_id = current.CollectionId", source);
        Assert.Contains("SourceContainerId = FirstText(qid, providerKey)", source);
        Assert.Contains("EquivalentContainerIds = BuildSequenceContainerAliases", source);
        Assert.Contains("ShouldMergeSequenceContainerOptions", source);
        Assert.Contains("SequenceContainerOptionMatches", source);
        Assert.Contains("PreferRoutableContainerId", source);
        Assert.DoesNotContain("key IN ('series', 'franchise')", source);
        Assert.DoesNotContain("GetDetailCanonicalValue(detail, MetadataFieldConstants.Franchise)", source);
        Assert.DoesNotContain("qid = GetDetailCanonicalValue(detail, \"franchise_qid\")", source);
        Assert.Contains("IsManifestItemInMediaScope", source);
        Assert.Contains("MediaType.Movies", hydratorSource);
        Assert.Contains("MediaType.Comics", hydratorSource);
        Assert.Contains("\"series_qid\"", hydratorSource);
        Assert.DoesNotContain("candidates.Count == 0 && mediaType is MediaType.Movies", hydratorSource);
        Assert.DoesNotContain("\"franchise_qid\"", hydratorSource);
        Assert.Contains("RelType, \"series\"", hydratorSource);
        Assert.DoesNotContain("context.MediaType is MediaType.Movies or MediaType.TV", hydratorSource);
        Assert.DoesNotContain("\"fictional_universe_qid\"", hydratorSource);
        Assert.Contains("LooksLikeEditionOrTranslation", hydratorSource);
    }

    [Fact]
    public void DetailComposer_FormatsRatingsAndUsesCreditImageFallbacks()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Api/Services/Details/DetailComposerService.cs"));

        Assert.Contains("FormatRating(detail.Rating)", source);
        Assert.Contains("ToString(\"0.0\"", source);
        Assert.Contains("canonicalArrayKey + MetadataFieldConstants.CompanionQidSuffix", source);
        Assert.Contains("headshot_url", source);
        Assert.Contains("GetValue(canonicalValues, $\"{canonicalArrayKey}_profile_url\")", source);
        Assert.Contains("Guest Stars", source);
        Assert.Contains("MetadataFieldConstants.GuestStar", source);
    }

    [Fact]
    public void DetailComposer_ExposesStructuredFactsOnUnifiedDetails()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Api/Services/Details/DetailComposerService.cs"));
        var contracts = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Contracts/Details/DetailDtos.cs"));
        var client = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Web/Services/Integration/EngineApiClient.cs"));

        Assert.Contains("public DetailFactsViewModel? Facts { get; init; }", contracts);
        Assert.Contains("public sealed class DetailFactsViewModel", contracts);
        Assert.Contains("public IReadOnlyDictionary<string, string> Identifiers { get; init; }", contracts);
        Assert.Contains("Facts = BuildWorkFacts(detail, entityType, values, contributorGroups)", source);
        Assert.Contains("Facts = BuildCollectionFacts(entityType, displayWorks, values, contributorGroups, row.WikidataQid)", source);
        Assert.Contains("Facts = BuildPersonFacts(person, displayRoles)", source);
        Assert.Contains("Actors = MergeNames(CreditNames(contributorGroups, CreditGroupType.Cast)", source);
        Assert.Contains("AlbumArtists = albumArtists", source);
        Assert.Contains("ShowName = FirstNonBlank(detail.ShowName", source);
        Assert.Contains("TrackNumber = FirstNonBlank(GetValue(canonicalValues, MetadataFieldConstants.TrackNumber)", source);
        Assert.Contains("Facts = detail.Facts", client);
    }

    [Fact]
    public void DetailComposer_UsesRootShowTitlesAndClaimFallbacksForPeople()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Api/Services/Details/DetailComposerService.cs"));
        var creditSource = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Api/Services/ReadServices/PersonCreditReadService.cs"));
        var scoringSource = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Providers/Services/ScoringHelper.cs"));
        var retailWorker = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Providers/Workers/RetailMatchWorker.cs"));
        var bridgeWorker = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Providers/Workers/WikidataBridgeWorker.cs"));
        var heroSource = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Web/Components/Details/DetailHero.razor"));

        Assert.Contains("ResolveCollectionTitle(entityType, row.DisplayName, rootValues, values)", source);
        Assert.Contains("GetValue(rootValues, MetadataFieldConstants.Title)", source);
        Assert.Contains("StripUniverseSuffix(displayName)", source);
        Assert.Contains("LoadContributorEntriesFromClaimsAsync", source);
        Assert.Contains("CreditGroupType.PrimaryArtists", source);
        Assert.Contains("CreditGroupType.MusicCredits", source);
        Assert.Contains("CreditGroupType.Illustrators", source);
        Assert.Contains("AddCreditLine(lines, \"Author\", CreditGroupType.Authors, CreditGroupType.Writers)", heroSource);
        Assert.Contains("AddCreditLine(lines, \"Illustrator\", CreditGroupType.Illustrators, CreditGroupType.CreativeTeam)", heroSource);
        Assert.Contains("BuildPersonCreditEntityId(credit.PersonId, credit.WikidataQid, credit.Name)", source);
        Assert.Contains("Subtitle = BuildPersonMediaCreditSubtitle(c)", source);
        Assert.Contains("FirstNonBlank(characterSummary, credit.Role)", source);
        Assert.Contains("ShouldShowContributorGroup(entityType, group)", source);
        Assert.Contains("return group.GroupType == CreditGroupType.Cast;", source);
        Assert.Contains("Title = \"Actors\"", source);
        Assert.Contains("CreditGroupType.Directors", source);

        Assert.Contains("BuildFallbackCreditsFromMetadataClaimsAsync", creditSource);
        Assert.Contains("mc.claim_key IN ('cast_member', 'cast_member_qid')", creditSource);
        Assert.Contains("ExtractQid(entry.ValueQid)", creditSource);

        Assert.Contains("BuildCanonicalArrayEntries(winningClaims, qidClaims)", scoringSource);
        Assert.Contains("ValueQid = parsed.Qid", scoringSource);
        Assert.Contains("arrayRepo: _arrayRepo", retailWorker);
        Assert.Contains("arrayRepo: _arrayRepo", bridgeWorker);
    }

    [Fact]
    public void DetailComposer_RelationshipChipRoutesOnlyUseKnownDestinations()
    {
        Assert.Equal("/universe/Q12345/explore", InvokePrivateString("BuildUniverseExploreRoute", "Q12345"));
        Assert.Equal("/universe/Q12345/explore", InvokePrivateString("BuildUniverseExploreRoute", "https://www.wikidata.org/wiki/Q12345"));
        Assert.Null(InvokePrivateString("BuildUniverseExploreRoute", "Sony Pictures"));

        var containerId = Guid.NewGuid();
        var sequence = new SequencePlacementViewModel
        {
            ContainerId = containerId.ToString("D"),
            CurrentItem = new SequenceItemViewModel { EntityType = DetailEntityType.Movie },
        };

        Assert.Equal($"/details/movieseries/{containerId:D}?context=watch", InvokePrivateString("BuildSequenceContainerRoute", sequence));
        Assert.Null(InvokePrivateString("BuildSequenceContainerRoute", new SequencePlacementViewModel
        {
            ContainerId = "Q999",
            CurrentItem = new SequenceItemViewModel { EntityType = DetailEntityType.Movie },
        }));
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

    private static string? InvokePrivateString(string methodName, params object?[] args)
    {
        var method = typeof(DetailComposerService).GetMethod(
            methodName,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);
        return (string?)method!.Invoke(null, args);
    }

    private static T InvokePrivate<T>(string methodName, params object?[] args)
    {
        var method = typeof(DetailComposerService).GetMethod(
            methodName,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);
        return Assert.IsType<T>(method!.Invoke(null, args));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MediaEngine.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
