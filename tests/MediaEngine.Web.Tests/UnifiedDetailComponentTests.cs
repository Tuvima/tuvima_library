namespace MediaEngine.Web.Tests;

public sealed class UnifiedDetailComponentTests
{
    [Fact]
    public void HeroBackdrop_RendersCentralizedHeroArtworkModes()
    {
        var source = ReadSource("src/MediaEngine.Web/Components/Details/HeroBackdrop.razor");
        var styles = ReadSource("src/MediaEngine.Web/Components/Details/DetailPage.razor.css");

        Assert.Contains("Artwork.HeroArtwork", source);
        Assert.Contains("HeroArtworkMode.BackdropWithLogo", source);
        Assert.Contains("HeroArtworkMode.BackdropWithRenderedTitle", source);
        Assert.Contains("HeroArtworkMode.ArtworkFallback", source);
        Assert.Contains("tl-detail-media-stage--background", source);
        Assert.Contains("tl-detail-media-stage--artwork-fallback", source);
        Assert.Contains("tl-detail-hero__artwork", source);
        Assert.Contains("tl-detail-hero__overlays", source);
        Assert.Contains("tl-detail-hero__foreground-art", source);
        Assert.Contains("tl-detail-media-stage__cover-atmosphere", source);
        Assert.Contains("tl-detail-media-stage__cover-wrap", source);
        Assert.Contains("tl-detail-media-stage__foreground", source);
        Assert.Contains("@onerror=\"HandleImageError\"", source);
        Assert.Contains("DetailEntityType.Book or DetailEntityType.Work => HeroForegroundTreatment.Book", source);
        Assert.Contains("DetailEntityType.Audiobook => HeroForegroundTreatment.Cover", source);
        Assert.Contains("DetailEntityType.Audiobook => \"tl-detail-media-stage--audiobook\"", source);

        Assert.Contains("tl-detail-media-stage--background .tl-detail-media-stage__overlay", styles);
        Assert.Contains("tl-detail-media-stage--artwork-fallback .tl-detail-media-stage__overlay", styles);
        Assert.Contains("to right", styles);
        Assert.Contains("rgba(var(--hero-shadow-rgb), 0.96) 0%", styles);
        Assert.Contains("ellipse at 72% 42%", styles);
        Assert.Contains("to bottom", styles);
        Assert.Contains("transparent 45%", styles);
        Assert.Contains("rgba(var(--hero-shadow-rgb), 0.95) 100%", styles);
        Assert.Contains("filter: blur(42px) saturate(1.18) brightness(0.72)", styles);
        Assert.Contains("opacity: 0.78", styles);
        Assert.Contains("transform: scale(1.08)", styles);
        Assert.Contains("object-fit: contain", styles);
        Assert.Contains("tl-detail-media-stage__foreground--cover .tl-detail-media-stage__cover", styles);
        Assert.Contains("opacity: 1", styles);
        Assert.DoesNotContain("opacity: 0.58", styles);
        Assert.Contains("width: fit-content", styles);
        Assert.Contains("height: 100%", styles);
        Assert.DoesNotContain("0 0 0 1px rgba(255, 255, 255, 0.10)", styles);
        Assert.Contains("content: none", styles);
        Assert.Contains("filter: none", styles);
        Assert.Contains("background-size: auto 100%", styles);
        Assert.Contains("tl-detail-hero--fallback-generated:not(.tl-detail-hero--watch)", styles);
        Assert.Contains("background-size: cover", styles);
        Assert.Contains("background-position: var(--hero-image-position, center right)", styles);
        Assert.Contains("background-position: right center", styles);
        Assert.DoesNotContain("background-size: contain", styles);
        Assert.Contains("tl-detail-hero--watch .tl-detail-hero__artwork", styles);
        Assert.DoesNotContain("tl-detail-hero--watch .tl-detail-hero__artwork::after", styles);
        Assert.Contains("min-height: clamp(680px, 86vh, 920px)", styles);
        Assert.DoesNotContain("tl-detail-backdrop", styles);
        Assert.DoesNotContain("tl-hero-art", styles);
        Assert.DoesNotContain("var(--tl-detail-accent) 30%", styles);
        Assert.DoesNotContain("linear-gradient(135deg, color-mix(in srgb, var(--tl-detail-primary) 36%, #101115)", styles);
    }

    [Fact]
    public void DetailHero_UsesCentralizedGradientVariablesAndProgress()
    {
        var presentation = ReadSource("src/MediaEngine.Web/Components/Details/DetailHeroPresentation.cs");
        var hero = ReadSource("src/MediaEngine.Web/Components/Details/DetailHero.razor");
        var progress = ReadSource("src/MediaEngine.Web/Components/Details/HeroProgressBlock.razor");
        var styles = ReadSource("src/MediaEngine.Web/Components/Details/DetailPage.razor.css");
        var client = ReadSource("src/MediaEngine.Web/Services/Integration/EngineApiClient.cs");
        var appStyles = ReadSource("src/MediaEngine.Web/wwwroot/app.css");
        var layout = ReadSource("src/MediaEngine.Web/Shared/MainLayout.razor");
        var layoutStyles = ReadSource("src/MediaEngine.Web/Shared/MainLayout.razor.css");

        Assert.Contains("tl-detail-hero--backdrop tl-detail-hero--backdrop-logo", presentation);
        Assert.Contains("tl-detail-hero--backdrop tl-detail-hero--backdrop-title", presentation);
        Assert.Contains("tl-detail-hero--watch", presentation);
        Assert.Contains("tl-detail-hero--read", presentation);
        Assert.Contains("tl-detail-hero--listen", presentation);
        Assert.Contains("tl-detail-hero--music", presentation);
        Assert.Contains("tl-detail-hero--fallback-generated", presentation);
        Assert.Contains("(R: 8, G: 12, B: 18)", presentation);
        Assert.Contains("(R: 4, G: 7, B: 12)", presentation);
        Assert.Contains("(R: 220, G: 165, B: 62)", presentation);
        Assert.Contains("--hero-shadow-rgb:0, 3, 5", presentation);
        Assert.Contains("ResolveSubtitle(model, isWatchHero)", presentation);
        Assert.Contains("isWatchHero || UsesPrimaryHeroChrome(model.EntityType)", presentation);
        Assert.Contains("<HeroProgressBlock Progress=\"Presentation.Progress\" />", hero);
        Assert.Contains("HeroCreditLines", hero);
        Assert.Contains("tl-detail-hero-credit-stack--audiobook", hero);
        Assert.Contains("CreditGroupType.Narrators", hero);
        Assert.Contains("CreditGroupType.Illustrators", hero);
        Assert.Contains("CreditGroupType.PrimaryArtists", hero);
        Assert.Contains("IsWatchHero=\"Presentation.IsWatchHero\"", hero);
        Assert.Contains("UsePrimaryHeroChrome=\"Presentation.UsePrimaryHeroChrome\"", hero);
        Assert.Contains("usePrimaryHeroChrome ? string.Empty : FormatEntityType", presentation);
        Assert.Contains("UsesPrimaryHeroChrome", presentation);
        Assert.Contains("tl-detail-hero-progress", progress);
        Assert.Contains("--hero-progress", progress);
        Assert.Contains("Progress = detail.Progress", client);
        Assert.Contains(".tl-detail-hero-progress__fill", styles);
        Assert.Contains("rgba(0, 3, 5, 0.34) 0%", styles);
        Assert.Contains("rgba(0, 3, 5, 0.10) 42%", styles);
        Assert.Contains("rgba(0, 3, 5, 0.98) 100%", styles);
        Assert.DoesNotContain("rgba(0, 0, 0, 0.10) 36%", styles);
        Assert.Contains("tl-detail-hero--watch .tl-detail-genre-chip", styles);
        Assert.Contains("background: rgba(20, 23, 28, 0.78)", styles);
        Assert.Contains("IsDetailShell", layout);
        Assert.Contains("layout-shell__appbar--detail", layout);
        Assert.Contains("MainContainerClass", layout);
        Assert.Contains("py-0", layout);
        Assert.Contains(".main-content-with-topbar--detail", layoutStyles);
        Assert.Contains(".layout-shell__appbar--detail", layoutStyles);
        Assert.Contains("body:has(.tl-detail-page) .main-content-with-topbar", appStyles);
        Assert.Contains(".layout-shell__appbar--detail", appStyles);
    }

    [Fact]
    public void DetailHero_RendersTvHeroBrandBadge()
    {
        var source = ReadSource("src/MediaEngine.Web/Components/Details/DetailHero.razor");
        var metadata = ReadSource("src/MediaEngine.Web/Components/Details/HeroMetadataPills.razor");
        var styles = ReadSource("src/MediaEngine.Web/Components/Details/DetailPage.razor.css");
        var client = ReadSource("src/MediaEngine.Web/Services/Integration/EngineApiClient.cs");

        Assert.Contains("Model.HeroBrand is not null", source);
        Assert.Contains("HeroBrand=\"Model.HeroBrand\"", source);
        Assert.Contains("RenderHeroBrandInMetadata=\"false\"", source);
        Assert.Contains("tl-detail-hero-brand", styles);
        Assert.Contains("ShouldSuppressTypeStat", metadata);
        Assert.Contains("Presentation.IsWatchHero", source);
        Assert.Contains("tl-detail-hero-brand img", styles);
        Assert.Contains("NormalizeHeroBrand", client);
        Assert.Contains("StreamingServiceLogoResolver", client);
        Assert.Contains("ResolveLogoPath(heroBrand.Label)", client);
    }

    [Fact]
    public void HeroMetadata_UsesInlineRowInsteadOfPills()
    {
        var source = ReadSource("src/MediaEngine.Web/Components/Details/HeroMetadataPills.razor");

        Assert.Contains("IsWatchHero", source);
        Assert.Contains("UsePrimaryHeroChrome", source);
        Assert.Contains("tl-detail-watch-metadata-row", source);
        Assert.Contains("IsWatchHeroStat", source);
        Assert.Contains("IsPrimaryChromeHeroStat", source);
        Assert.DoesNotContain("or \"type\" or \"genre\"", source);
        Assert.Contains("Icons.Material.Filled.Star", source);
        Assert.Contains("tl-detail-watch-metadata-item--rating", source);
        Assert.Contains("tl-detail-metadata-row", source);
        Assert.Contains("tl-detail-metadata-item", source);
        Assert.Contains("tl-detail-metadata-item--rating", source);
        Assert.DoesNotContain("tl-detail-pill", source);
    }

    [Fact]
    public void HeroActions_ExposeHoverMenusAndCapabilityAwareActions()
    {
        var source = ReadSource("src/MediaEngine.Web/Components/Details/HeroActionRow.razor");
        var composer = ReadSource("src/MediaEngine.Api/Services/Details/DetailComposerService.cs");
        var styles = ReadSource("src/MediaEngine.Web/Components/Details/DetailPage.razor.css");

        Assert.Contains("tl-reaction-menu", source);
        Assert.Contains("tl-detail-reaction-button", source);
        Assert.Contains("tl-detail-watch-secondary tl-detail-watch-secondary--icon tl-detail-reaction-button", source);
        Assert.Contains("OrderedReactionChildren", source);
        Assert.Contains("\"reaction-dislike\" => 0", source);
        Assert.Contains("\"reaction-like\" => 1", source);
        Assert.Contains("tl-detail-premium-action", source);
        Assert.Contains("tl-detail-action--secondary-button", source);
        Assert.Contains("tl-detail-actions--watch", source);
        Assert.Contains("tl-detail-watch-secondary", source);
        Assert.Contains("UsePrimaryHeroChrome && action.Key == \"read-listen\"", source);
        Assert.Contains("UsePrimaryHeroChrome && action.Key == \"add-to-collection\"", source);
        Assert.Contains("read-listen", composer);
        Assert.DoesNotContain("Key = \"preview\",", composer);
        Assert.Contains("IsReadableEntity", composer);
        Assert.Contains("SupportsWatchParty", composer);
        Assert.Contains("watch-party", composer);
        Assert.Contains("Tooltip = \"Watch Party setup is coming soon\"", composer);
        Assert.Contains("IsStub = true", composer);
        Assert.Contains("Label = \"Watchlist\"", composer);
        Assert.Contains("=> \"Want to Read\"", composer);
        Assert.Contains("=> \"Want to Listen\"", composer);
        Assert.Contains("BuildReactionAction", composer);
        Assert.Contains("border-radius: 0.5rem", styles);
        Assert.Contains("tl-detail-reaction-button", styles);
        Assert.Contains("border-radius: 0.75rem", styles);
        Assert.Contains("tl-detail-hero--read:not(.tl-detail-hero--watch) .tl-detail-genre-chip", styles);
        Assert.Contains("tl-detail-hero--fallback-surface:not(.tl-detail-hero--watch) .tl-detail-genre-chip", styles);
        Assert.Contains(".tl-detail-actions--watch .tl-detail-action--primary", styles);
        Assert.Contains("tl-detail-hero--read:not(.tl-detail-hero--watch) .tl-detail-actions--watch .tl-detail-action--primary", styles);
        Assert.Contains("tl-detail-hero--read:not(.tl-detail-hero--watch)", styles);
        Assert.Contains("tl-detail-hero-credit-stack--audiobook", styles);
        Assert.Contains("::deep .tl-detail-hero-credit-stack__line", styles);
        Assert.Contains("letter-spacing: 0.42em", styles);
        Assert.Contains("tl-detail-media-stage--book.tl-detail-media-stage--cover-fallback", styles);
        Assert.Contains("overflow: visible", styles);
        Assert.Contains("height: min(47rem, 78vh)", styles);
        Assert.Contains("height: 100%", styles);
        Assert.Contains("max-height: min(47rem, 78vh)", styles);
        Assert.DoesNotContain("&& SupportsWatchParty(entityType)", composer);
    }

    [Fact]
    public void DetailPage_WiresReadAndReactionHeroActions()
    {
        var source = ReadSource("src/MediaEngine.Web/Components/Details/DetailPage.razor");

        Assert.Contains("ResolveWorkToAssetAsync", source);
        Assert.Contains("Nav.NavigateTo($\"/read/{assetId.Value:D}\")", source);
        Assert.Contains("MediaReactionService Reactions", source);
        Assert.Contains("SetReactionAsync(action.Key == \"like\" ? MediaReaction.Like : MediaReaction.Dislike)", source);
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
    public void OverviewTab_CombinesOverviewWithCredits()
    {
        var source = ReadSource("src/MediaEngine.Web/Components/Details/OverviewTab.razor");
        var styles = ReadSource("src/MediaEngine.Web/Components/Details/DetailPage.razor.css");

        Assert.Contains("tl-media-overview-panel", source);
        Assert.Contains("tl-media-overview-card", source);
        Assert.Contains("tl-media-overview-card__top", source);
        Assert.Contains("tl-media-overview-card__summary", source);
        Assert.Contains("tl-media-overview-card__credits", source);
        Assert.Contains("SeriesPlacementPanel", source);
        Assert.Contains("Compact=\"true\"", source);
        Assert.Contains("MoreLikeThisItems", source);
        Assert.Contains("IsRecommendationGroup", source);
        Assert.Contains("IsRecommendationCandidate", source);
        Assert.Contains("item.EntityType is not DetailEntityType.TvEpisode", source);
        Assert.Contains("More Like This", source);
        Assert.DoesNotContain("tl-media-overview-card__art", source);
        Assert.Contains("BuildVideoCreditGroups", source);
        Assert.Contains("IsTv ? [] : CreditsFor(CreditGroupType.Directors)", source);
        Assert.Contains("new OverviewCreditGroup(\"Cast\"", source);
        Assert.Contains("tl-media-overview-credits-list--bookish", source);
        Assert.Contains("tl-media-overview-credits-list--video", source);
        Assert.Contains("View all cast", source);
        Assert.Contains("View all credits", source);
        Assert.Contains("CreditGroupType.Authors", source);
        Assert.Contains("CreditGroupType.Narrators", source);
        Assert.Contains("CreditGroupType.Writers", source);
        Assert.Contains("CreditGroupType.Illustrators", source);
        Assert.DoesNotContain("RelatedEntityChip", source);
        Assert.Contains("OverviewParagraphs(Model.Description)", source);
        Assert.Contains("Split([\"\\n\\n\"]", source);
        Assert.Contains("Replace(\"\\\\n\", \"\\n\", StringComparison.Ordinal)", source);
        Assert.Contains("DescriptionAttribution Attribution=\"Model.DescriptionAttribution\" Compact=\"true\"", source);
        Assert.Contains("width: min(90vw, calc(100% - clamp(2rem, 4vw, 4rem)))", styles);
        Assert.Contains(".tl-detail-tab-panel.tl-media-overview-panel", styles);
        Assert.Contains("grid-template-columns: 1fr", styles);
        Assert.Contains(".tl-media-overview-card__top", styles);
        Assert.Contains(".tl-media-overview-card__series .tl-series-placement--compact", styles);
        Assert.Contains(".tl-overview-related-strip", styles);
        Assert.Contains(".tl-overview-credit-grid", styles);
        Assert.Contains(".tl-media-overview-credits-list--bookish", styles);
        Assert.Contains(".tl-media-overview-credits-list--video", styles);
        Assert.Contains("font-size: 0.96rem", styles);
        Assert.Contains("white-space: pre-line", styles);
        Assert.Contains(".tl-detail-copy p", styles);
    }

    [Fact]
    public void DetailHero_PutsOverflowActionsInHeroActionRow()
    {
        var hero = ReadSource("src/MediaEngine.Web/Components/Details/DetailHero.razor");
        var actions = ReadSource("src/MediaEngine.Web/Components/Details/HeroActionRow.razor");
        var menu = ReadSource("src/MediaEngine.Web/Components/Details/OverflowActionMenu.razor");
        var menuItems = ReadSource("src/MediaEngine.Web/Components/Details/ManageActionsMenu.razor");
        var detailPage = ReadSource("src/MediaEngine.Web/Components/Details/DetailPage.razor");
        var appStyles = ReadSource("src/MediaEngine.Web/wwwroot/app.css");

        Assert.Contains("OverflowActions=\"Model.OverflowActions\"", hero);
        Assert.Contains("OverflowActionMenu", actions);
        Assert.Contains("class=\"tl-detail-overflow-popover\"", menu);
        Assert.Contains("@onclick=\"ToggleMenu\"", menu);
        Assert.Contains("aria-expanded=\"@_isOpen\"", menu);
        Assert.Contains("class=\"tl-detail-overflow-list\"", menuItems);
        Assert.Contains("role=\"menuitem\"", menuItems);
        Assert.Contains("disabled=\"@action.IsDisabled\"", menuItems);
        Assert.DoesNotContain("MudMenuItem", menuItems);
        Assert.Contains("action.Key is \"edit-media\" or \"edit\"", detailPage);
        Assert.Contains("action.Key == \"details\"", detailPage);
        Assert.Contains("SetActiveTabAsync(\"details\")", detailPage);
        Assert.Contains(".tl-detail-overflow-popover", appStyles);
        Assert.Contains(".tl-detail-overflow-item", appStyles);
        Assert.Contains("border: 0;", appStyles);
    }

    [Fact]
    public void PeopleStrip_RemainsOutOfHeroAndSharedAvatarFallbackStaysAvailable()
    {
        var detailPage = ReadSource("src/MediaEngine.Web/Components/Details/DetailPage.razor");
        var hero = ReadSource("src/MediaEngine.Web/Components/Details/DetailHero.razor");
        var strip = ReadSource("src/MediaEngine.Web/Components/Details/PeoplePreviewStrip.razor");
        var card = ReadSource("src/MediaEngine.Web/Components/Details/PersonCreditCard.razor");
        var avatar = ReadSource("src/MediaEngine.Web/Components/Details/PersonAvatar.razor");
        var group = ReadSource("src/MediaEngine.Web/Components/Details/CreditGroupSection.razor");
        var appStyles = ReadSource("src/MediaEngine.Web/Components/Details/DetailPage.razor.css");

        Assert.Contains("<DetailHero Model=\"Model\"", detailPage);
        Assert.DoesNotContain("<PeoplePreviewStrip", detailPage);
        Assert.DoesNotContain("PreviewContributors", hero);
        Assert.Contains("Compact=\"true\"", strip);
        Assert.Contains("PersonAvatar", card);
        Assert.Contains("@onerror=\"HandleImageError\"", avatar);
        Assert.Contains("tl-credit-group__toggle", group);
        Assert.Contains(".tl-credit-card__image .tl-person-avatar", appStyles);
        Assert.Contains("aspect-ratio: 1 / 1", appStyles);
        Assert.Contains("linear-gradient(to top", appStyles);
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

        Assert.Contains("<h2>@SeriesTitleDisplay</h2>", source);
        Assert.Contains("[Parameter] public bool Compact { get; set; }", source);
        Assert.Contains("tl-series-placement--compact", source);
        Assert.Contains("@(Compact ? SeriesTitleDisplay : \"Series\")", source);
        Assert.Contains("item.IsCurrent && !Compact", source);
        Assert.DoesNotContain("Part of", source);
        Assert.Contains("TitleCaseDisplay(Placement.SeriesTitle)", source);
        Assert.Contains("VisibleItems", source);
        Assert.Contains("tl-series-carousel__arrow", source);
        Assert.Contains("MudChart T=\"double\"", source);
        Assert.Contains("ChartType=\"ChartType.Donut\"", source);
        Assert.Contains("ChartSeries=\"@SeriesDonutSeries\"", source);
        Assert.Contains("ChartLabels=\"@SeriesDonutLabels\"", source);
        Assert.DoesNotContain("View all", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("View Full Series", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tl-series-view-all", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tl-series-position-summary", source);
        Assert.Contains("tl-series-position-summary__donut", source);
        Assert.Contains("tl-series-position-summary__center", source);
        Assert.DoesNotContain("Current position", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tl-series-item__current-badge", source);
        Assert.Contains("tl-series-item__node", source);
        Assert.Contains("SeriesItemRoute(item)", source);
        Assert.Contains("href=\"@route\"", source);
        Assert.Contains("private int TotalItems => Placement.OrderedItems.Count", source);
        Assert.Contains("tl-series-placement--long", source);
        Assert.Contains("LongSeriesThreshold = 9", source);
        Assert.DoesNotContain("tl-series-item__owned-badge", source);
        Assert.DoesNotContain("Icons.Material.Filled.Check\" Size=\"Size.Small\"", source);
        Assert.Contains("ItemNoun", source);
        Assert.Contains("grid-template-columns: repeat(var(--series-count, 6), minmax(5.8rem, 7.3rem))", styles);
        Assert.Contains("grid-template-columns: repeat(var(--series-count, 7), minmax(7.2rem, 9.2rem))", styles);
        Assert.Contains("scrollbar-width: none", styles);
        Assert.Contains("tl-series-placement--long .tl-series-strip::before", styles);
        Assert.Contains("content: none", styles);
        Assert.Contains("min-width: clamp(5.8rem, 7.2vw, 7.3rem)", styles);
        Assert.Contains("tl-series-item.is-current .tl-series-item__art", styles);
        Assert.Contains("tl-series-item__current-badge", styles);
        Assert.DoesNotContain("tl-series-item__owned-badge", styles);
        Assert.Contains("tl-series-owned-summary", styles);
        Assert.Contains("minmax(3rem, auto)", styles);
        Assert.Contains("-webkit-line-clamp: 4", styles);
        Assert.Contains("a.tl-series-item", styles);
        Assert.Contains("padding-bottom: 1.4rem", styles);
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
    public void RoutePages_AcceptOptionalDetailTabSegments()
    {
        var book = ReadSource("src/MediaEngine.Web/Components/Pages/BookDetail.razor");
        var unified = ReadSource("src/MediaEngine.Web/Components/Pages/UnifiedDetailPage.razor");
        var movie = ReadSource("src/MediaEngine.Web/Components/Pages/WatchMoviePage.razor");
        var show = ReadSource("src/MediaEngine.Web/Components/Pages/WatchTvShowPage.razor");
        var episode = ReadSource("src/MediaEngine.Web/Components/Pages/WatchTvEpisodePage.razor");

        Assert.Contains("@page \"/book/{Id:guid}/{Tab}\"", book);
        Assert.Contains("@page \"/details/{EntityType}/{Id:guid}/{Tab}\"", unified);
        Assert.Contains("@page \"/watch/movie/{WorkId:guid}/{Tab}\"", movie);
        Assert.Contains("@page \"/watch/tv/show/{CollectionId:guid}/{Tab}\"", show);
        Assert.Contains("@page \"/watch/tv/show/{CollectionId:guid}/episode/{WorkId:guid}/{Tab}\"", episode);
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
    public void MusicAlbumDetailsUseSharedTrackList()
    {
        var detailPage = ReadSource("src/MediaEngine.Web/Components/Details/DetailPage.razor");
        var trackList = ReadSource("src/MediaEngine.Web/Components/Details/MusicTrackList.razor");
        var albumRoute = ReadSource("src/MediaEngine.Web/Components/Pages/UnifiedDetailPage.razor");

        Assert.Contains("MusicTrackList", detailPage);
        Assert.Contains("ReplaceQueueItemsAsync", trackList);
        Assert.Contains("tl-detail-track-list", trackList);
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
