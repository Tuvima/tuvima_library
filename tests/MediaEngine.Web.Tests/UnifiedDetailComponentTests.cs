namespace MediaEngine.Web.Tests;

public sealed class UnifiedDetailComponentTests
{
    [Fact]
    public void HeroBackdrop_RendersCentralizedHeroArtworkModes()
    {
        var source = ReadSource("src/MediaEngine.Web/Components/Details/HeroBackdrop.razor");
        var styles = ReadSource("src/MediaEngine.Web/Components/Details/DetailPage.razor.css");

        Assert.Contains("Artwork.HeroArtwork", source);
        Assert.Contains("HeroArtworkMode.Background", source);
        Assert.Contains("HeroArtworkMode.CoverFallback", source);
        Assert.Contains("tl-detail-media-stage--background", source);
        Assert.Contains("tl-detail-media-stage--cover-fallback", source);
        Assert.Contains("tl-detail-media-stage__cover-atmosphere", source);
        Assert.Contains("tl-detail-media-stage__cover-wrap", source);
        Assert.Contains("@onerror=\"HandleImageError\"", source);

        Assert.Contains("tl-detail-media-stage--background .tl-detail-media-stage__overlay", styles);
        Assert.Contains("tl-detail-media-stage--cover-fallback .tl-detail-media-stage__overlay", styles);
        Assert.Contains("filter: blur(64px) saturate(1.15)", styles);
        Assert.Contains("opacity: 0.28", styles);
        Assert.Contains("transform: scale(1.35)", styles);
        Assert.Contains("object-fit: contain", styles);
        Assert.Contains("opacity: 1", styles);
        Assert.DoesNotContain("opacity: 0.58", styles);
        Assert.Contains("width: fit-content", styles);
        Assert.Contains("height: 100%", styles);
        Assert.DoesNotContain("0 0 0 1px rgba(255, 255, 255, 0.10)", styles);
        Assert.Contains("content: none", styles);
        Assert.Contains("filter: none", styles);
        Assert.Contains("rgba(var(--hero-bg-rgb), 0.98) 0%", styles);
        Assert.Contains("inset: 0 0 0 34%", styles);
        Assert.Contains("width: 66%", styles);
        Assert.Contains("height: 100%", styles);
        Assert.Contains("object-position: var(--hero-image-position, center right)", styles);
        Assert.Contains("object-fit: contain", styles);
        Assert.Contains("mask-image:", styles);
        Assert.Contains("transparent 0%", styles);
        Assert.DoesNotContain("background-size: contain", styles);
        Assert.DoesNotContain("tl-detail-media-stage__background {\r\n    position: absolute;\r\n    inset: 0;\r\n    width: 100%;\r\n    height: 100%;\r\n    object-fit: cover", styles);
        Assert.Contains("min-height: clamp(42rem, 84vh, 64rem)", styles);
        Assert.DoesNotContain("tl-detail-backdrop", styles);
        Assert.DoesNotContain("tl-hero-art", styles);
        Assert.DoesNotContain("var(--tl-detail-accent) 30%", styles);
        Assert.DoesNotContain("linear-gradient(135deg, color-mix(in srgb, var(--tl-detail-primary) 36%, #101115)", styles);
    }

    [Fact]
    public void HeroBackdrop_RendersTvHeroBrand()
    {
        var source = ReadSource("src/MediaEngine.Web/Components/Details/HeroBackdrop.razor");
        var styles = ReadSource("src/MediaEngine.Web/Components/Details/DetailPage.razor.css");
        var client = ReadSource("src/MediaEngine.Web/Services/Integration/EngineApiClient.cs");

        Assert.Contains("HeroBrandViewModel", source);
        Assert.Contains("tl-detail-hero-brand", source);
        Assert.Contains("DetailEntityType.TvShow", source);
        Assert.Contains("tl-detail-hero-brand img", styles);
        Assert.Contains("NormalizeHeroBrand", client);
    }

    [Fact]
    public void HeroMetadata_UsesInlineRowInsteadOfPills()
    {
        var source = ReadSource("src/MediaEngine.Web/Components/Details/HeroMetadataPills.razor");

        Assert.Contains("tl-detail-metadata-row", source);
        Assert.Contains("tl-detail-metadata-item", source);
        Assert.Contains("tl-detail-metadata-item--rating", source);
        Assert.DoesNotContain("tl-detail-pill", source);
    }

    [Fact]
    public void HeroActions_ExposeHoverMenusAndWatchPartyStub()
    {
        var source = ReadSource("src/MediaEngine.Web/Components/Details/HeroActionRow.razor");
        var composer = ReadSource("src/MediaEngine.Api/Services/Details/DetailComposerService.cs");

        Assert.Contains("tl-reaction-menu", source);
        Assert.Contains("tl-format-menu", source);
        Assert.Contains("watch-party", composer);
        Assert.Contains("IsStub = true", composer);
    }

    [Fact]
    public void DetailPage_RendersExpectedSpecializedTabs()
    {
        var source = ReadSource("src/MediaEngine.Web/Components/Details/DetailPage.razor");

        Assert.Contains("EpisodesTab", source);
        Assert.Contains("FormatsTab", source);
        Assert.Contains("SyncTab", source);
        Assert.Contains("RegistryTab", source);
    }

    [Fact]
    public void OverviewTab_DoesNotRepeatContributorsOrCharacters()
    {
        var source = ReadSource("src/MediaEngine.Web/Components/Details/OverviewTab.razor");
        var styles = ReadSource("src/MediaEngine.Web/Components/Details/DetailPage.razor.css");

        Assert.DoesNotContain("ContributorsSection", source);
        Assert.DoesNotContain("CharactersSection", source);
        Assert.DoesNotContain("RelatedEntityChip", source);
        Assert.Contains("OverviewParagraphs(Model.Description)", source);
        Assert.Contains("Split([\"\\n\\n\"]", source);
        Assert.Contains("Replace(\"\\\\n\", \"\\n\", StringComparison.Ordinal)", source);
        Assert.Contains("white-space: pre-line", styles);
        Assert.Contains(".tl-detail-copy p", styles);
    }

    [Fact]
    public void DetailHero_PutsOverflowActionsInHeroActionRow()
    {
        var hero = ReadSource("src/MediaEngine.Web/Components/Details/DetailHero.razor");
        var actions = ReadSource("src/MediaEngine.Web/Components/Details/HeroActionRow.razor");

        Assert.Contains("OverflowActions=\"Model.OverflowActions\"", hero);
        Assert.Contains("OverflowActionMenu", actions);
    }

    [Fact]
    public void PeopleStrip_StaysBelowHeroAndUsesSharedAvatarFallback()
    {
        var detailPage = ReadSource("src/MediaEngine.Web/Components/Details/DetailPage.razor");
        var hero = ReadSource("src/MediaEngine.Web/Components/Details/DetailHero.razor");
        var strip = ReadSource("src/MediaEngine.Web/Components/Details/PeoplePreviewStrip.razor");
        var card = ReadSource("src/MediaEngine.Web/Components/Details/PersonCreditCard.razor");
        var avatar = ReadSource("src/MediaEngine.Web/Components/Details/PersonAvatar.razor");
        var group = ReadSource("src/MediaEngine.Web/Components/Details/CreditGroupSection.razor");

        Assert.Contains("<DetailHero Model=\"Model\"", detailPage);
        Assert.Contains("<PeoplePreviewStrip Title=\"@CreditPreviewTitle(Model)\"", detailPage);
        Assert.DoesNotContain("PreviewContributors", hero);
        Assert.Contains("Compact=\"true\"", strip);
        Assert.Contains("PersonAvatar", card);
        Assert.Contains("@onerror=\"HandleImageError\"", avatar);
        Assert.Contains("tl-credit-group__toggle", group);
    }

    [Fact]
    public void EngineClient_NormalizesSeriesAndCreditImageUrls()
    {
        var source = ReadSource("src/MediaEngine.Web/Services/Integration/EngineApiClient.cs");

        Assert.Contains("NormalizeSeriesPlacement", source);
        Assert.Contains("NormalizeSeriesItem", source);
        Assert.Contains("NormalizeHeroArtwork", source);
        Assert.Contains("ImageUrl = NormalizeOptionalUrl(credit.ImageUrl)", source);
    }

    [Fact]
    public void SeriesPlacementPanel_DoesNotRenderViewAllLink()
    {
        var source = ReadSource("src/MediaEngine.Web/Components/Details/SeriesPlacementPanel.razor");
        var styles = ReadSource("src/MediaEngine.Web/Components/Details/DetailPage.razor.css");

        Assert.Contains("Part of @Placement.SeriesTitle", source);
        Assert.DoesNotContain("View all", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tl-series-view-all", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DetailEntityType.Movie or DetailEntityType.MovieSeries", source);
        Assert.Contains("left: 0.45rem", styles);
        Assert.Contains("background: rgba(5,8,12,0.88)", styles);
    }

    [Fact]
    public void RoutePages_DoNotChooseHeroArtwork()
    {
        var root = FindRepoRoot();
        var detailRoutes = new[]
        {
            "WatchMoviePage.razor",
            "WatchTvShowPage.razor",
            "WatchTvEpisodePage.razor",
            "BookDetail.razor",
            "CollectionDetail.razor",
            "PersonDetail.razor",
            "UnifiedDetailPage.razor",
        };
        var pageSources = detailRoutes
            .Select(path => File.ReadAllText(Path.Combine(root, "src/MediaEngine.Web/Components/Pages", path)));

        foreach (var source in pageSources)
        {
            Assert.DoesNotContain("HeroArtwork", source);
            Assert.DoesNotContain("BackdropUrl", source);
            Assert.DoesNotContain("CoverUrl", source);
        }
    }

    [Fact]
    public void DetailComposer_UsesWikidataDescriptionForHeroSummary()
    {
        var source = ReadSource("src/MediaEngine.Api/Services/Details/DetailComposerService.cs");

        Assert.Contains("BuildHeroSummaryAsync", source);
        Assert.Contains("MetadataFieldConstants.ShortDescription", source);
        Assert.Contains("wikidata_description", source);
        Assert.Contains("wikidata_summary", source);
    }

    [Fact]
    public void ExistingMusicAlbumExperience_RemainsOnListenPage()
    {
        var listenPage = ReadSource("src/MediaEngine.Web/Components/Pages/ListenPage.razor");
        var albumRoute = ReadSource("src/MediaEngine.Web/Components/Pages/UnifiedDetailPage.razor");

        Assert.Contains("album", listenPage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@page \"/listen/album", albumRoute, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadSource(string relativePath)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, relativePath));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MediaEngine.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
