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
        Assert.DoesNotContain("tl-detail-media-stage__background-fill", source);
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
        Assert.Contains("object-fit: contain", styles);
        Assert.Contains("tl-detail-hero--fallback-generated:not(.tl-detail-hero--watch)", styles);
        Assert.Contains("background-size: cover", styles);
        Assert.Contains("object-position: var(--hero-image-position, center right)", styles);
        Assert.Contains("object-position: right center", styles);
        Assert.DoesNotContain("tl-detail-media-stage__background-fill", styles);
        Assert.DoesNotContain("tl-detail-media-stage--background::before", styles);
        Assert.Contains("mask-image: linear-gradient(to right", styles);
        Assert.Contains("background-size: cover", styles);
        Assert.Contains("tl-detail-hero--watch .tl-detail-hero__artwork", styles);
        Assert.DoesNotContain("tl-detail-hero--watch .tl-detail-hero__artwork::after", styles);
        Assert.Contains("height: calc(80svh - var(--app-topbar-height, 65px) - 1rem)", styles);
        Assert.Contains("min-height: 28rem", styles);
        Assert.Contains("max-height: none", styles);
        Assert.Contains("rgba(0,0,0,0.76) 43%", styles);
        Assert.Contains("tl-detail-tabs::before", styles);
        Assert.Contains("background: #090c12", styles);
        Assert.Contains("font-family: Georgia, \"Times New Roman\", serif", styles);
        Assert.Contains("font-weight: 500", styles);
        Assert.Contains("max-width: 22ch", styles);
        Assert.DoesNotContain("min-height: calc(100dvh - 5.75rem)", styles);
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
        Assert.Contains("rgba(var(--hero-bg-rgb), 0.55) 37%", styles);
        Assert.Contains("transparent 47%", styles);
        Assert.DoesNotContain("rgba(0, 0, 0, 0.10) 36%", styles);
        Assert.Contains("tl-detail-hero--watch .tl-detail-genre-chip", styles);
        Assert.Contains("background: rgba(20, 23, 28, 0.78)", styles);
        Assert.Contains("border: 1px solid rgba(216, 180, 254, 0.88)", styles);
        Assert.Contains("linear-gradient(180deg, var(--tl-accent-primary, #8b5cf6) 0%, var(--tl-accent-primary-active, #7652d6) 100%)", styles);
        Assert.Contains("0 0 28px rgba(139, 92, 246, 0.48)", styles);
        Assert.Contains("IsDetailShell", layout);
        Assert.Contains("MainContainerClass", layout);
        Assert.Contains("py-0", layout);
        Assert.Contains("private const string AppBarClass = \"layout-shell__appbar\"", layout);
        Assert.Contains("private const string MainContentClass = \"main-content-with-topbar\"", layout);
        Assert.DoesNotContain("main-content-with-topbar--detail", layoutStyles);
        Assert.DoesNotContain("layout-shell__appbar--detail", layoutStyles);
        Assert.DoesNotContain("body:has(.tl-detail-page) .main-content-with-topbar", appStyles);
        Assert.DoesNotContain("body:has(.tl-detail-page) .layout-shell__appbar,", appStyles);
        Assert.Contains("background: var(--app-surface, #1e1f27) !important", layoutStyles);
        Assert.Contains("width: clamp(74%, 80vw, 88%)", styles);
        Assert.Contains("object-position: right top", styles);
        Assert.Contains("tl-detail-hero--backdrop .tl-detail-hero__inner", styles);
        Assert.Contains("align-items: center", styles);
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

        Assert.Contains("tl-detail-premium-action", source);
        Assert.Contains("tl-detail-action--secondary-button", source);
        Assert.Contains("tl-detail-actions--watch", source);
        Assert.Contains("tl-detail-watch-secondary", source);
        Assert.Contains("VisibleSecondaryActions", source);
        Assert.Contains("favorite_filled", source);
        Assert.Contains("Icons.Material.Filled.Favorite", source);
        Assert.Contains("UsePrimaryHeroChrome && action.Key == \"read-listen\"", source);
        Assert.Contains("UsePrimaryHeroChrome && action.Key == \"add-to-collection\"", source);
        Assert.Contains("read-listen", composer);
        Assert.DoesNotContain("Key = \"preview\",", composer);
        Assert.Contains("IsReadableEntity", composer);
        Assert.Contains("SupportsWatchParty", composer);
        Assert.Contains("watch-party", composer);
        Assert.Contains("Tooltip = \"Watch Party setup is coming soon\"", composer);
        Assert.Contains("IsStub = true", composer);
        Assert.Contains("Label = isSelected ? \"In My List\" : \"My List\"", composer);
        Assert.Contains("Key = \"my-list\"", composer);
        Assert.Contains("Label = \"Add to Collection\"", composer);
        Assert.Contains("BuildMyListAction", composer);
        Assert.Contains("BuildReactionAction", composer);
        Assert.Contains("reaction-menu", composer);
        Assert.Contains("reaction-dislike", composer);
        Assert.Contains("reaction-like", composer);
        Assert.Contains("reaction-love", composer);
        Assert.Contains("tl-detail-flat-action", source);
        Assert.Contains("ReactionStateClass", source);
        Assert.Contains("border-radius: 0.5rem", styles);
        Assert.Contains("border-radius: 0.75rem", styles);
        Assert.Contains("tl-detail-hero--read:not(.tl-detail-hero--watch) .tl-detail-genre-chip", styles);
        Assert.Contains("tl-detail-hero--fallback-surface:not(.tl-detail-hero--watch) .tl-detail-genre-chip", styles);
        Assert.Contains(".tl-detail-actions--watch .tl-detail-action--primary", styles);
        Assert.Contains(".tl-detail-watch-secondary.is-selected", styles);
        Assert.Contains("tl-detail-hero--read:not(.tl-detail-hero--watch) .tl-detail-actions--watch .tl-detail-action--primary", styles);
        Assert.Contains("tl-detail-hero--read:not(.tl-detail-hero--watch)", styles);
        Assert.Contains("tl-detail-hero-credit-stack--audiobook", styles);
        Assert.Contains("::deep .tl-detail-hero-credit-stack__line", styles);
        Assert.Contains("letter-spacing: 0.03em", styles);
        Assert.Contains("tl-detail-media-stage--book.tl-detail-media-stage--cover-fallback", styles);
        Assert.Contains("overflow: visible", styles);
        Assert.Contains("height: min(47rem, 78vh)", styles);
        Assert.Contains("height: 100%", styles);
        Assert.Contains("max-height: min(47rem, 78vh)", styles);
        Assert.DoesNotContain("&& SupportsWatchParty(entityType)", composer);
    }

    [Fact]
    public void DetailPage_WiresReadAndFavoriteHeroActions()
    {
        var source = ReadSource("src/MediaEngine.Web/Components/Details/DetailPage.razor");

        Assert.Contains("ResolveWorkToAssetAsync", source);
        Assert.Contains("Nav.NavigateTo($\"/read/{assetId.Value:D}\")", source);
        Assert.Contains("FavoriteService Favorites", source);
        Assert.Contains("ToggleFavoriteAsync(action)", source);
        Assert.Contains("MediaReactionService Reactions", source);
        Assert.Contains("SetReactionAsync(action)", source);
        Assert.Contains("action.Key == \"my-list\"", source);
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
    public void EpisodesTab_UsesSeasonSelectorWhenMultipleSeasonsExist()
    {
        var source = ReadSource("src/MediaEngine.Web/Components/Details/EpisodesTab.razor");

        var styles = ReadSource("src/MediaEngine.Web/Components/Details/DetailPage.razor.css");

        Assert.Contains("VisibleGroups.Count > 0", source);
        Assert.Contains("Disabled=\"@(VisibleGroups.Count == 1)\"", source);
        Assert.Contains("<AppNativeSelect Value=\"@SelectedKey\" OnChange=\"SelectSeason\"", source);
        Assert.Contains("<option value=\"@group.Key\">@group.Title</option>", source);
        Assert.Contains("VisibleGroups[0].Key", source);
        Assert.Contains("episode.IsOwned && episode.Actions.Any", source);
        Assert.Contains("tl-episode-grid", source);
        Assert.Contains("tl-episode-card__play", source);
        Assert.Contains("grid-template-columns: repeat(4, minmax(0, 1fr))", styles);
        Assert.Contains("var(--tl-accent-primary, #8b5cf6)", styles);
    }

    [Fact]
    public void DetailPage_CastTabDoesNotInlineCharacters()
    {
        var source = ReadSource("src/MediaEngine.Web/Components/Details/DetailPage.razor");
        var peopleTab = ReadSource("src/MediaEngine.Web/Components/Details/PeopleAndCharactersTab.razor");

        Assert.Contains("IncludeCharacters=\"IncludeCharactersInPeopleTab\"", source);
        Assert.Contains("CurrentActiveTab is not \"cast\"", source);
        Assert.Contains("IncludeCharacters && CharacterGroups.Count > 0", peopleTab);
    }

    [Fact]
    public void CreditsTab_UsesCastLikeFluidCreditCards()
    {
        var styles = ReadSource("src/MediaEngine.Web/Components/Details/DetailPage.razor.css");

        Assert.Contains(".tl-credits-detail-card .tl-credit-grid", styles);
        Assert.Contains("grid-template-columns: repeat(auto-fill, minmax(8.5rem, 1fr))", styles);
        Assert.Contains("align-content: start", styles);
        Assert.Contains("align-items: start", styles);
        Assert.Contains("align-self: start", styles);
        Assert.Contains("max-width: 12rem", styles);
        Assert.DoesNotContain("grid-template-columns: repeat(auto-fill, minmax(8.75rem, 9.75rem))", styles);
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
        Assert.DoesNotContain("SequencePlacementPanel", source);
        Assert.DoesNotContain("SequencePlacementPanel Placement=", source);
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
        Assert.Contains("Split('\\n'", source);
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
        Assert.Contains("VisibleSecondaryActions", actions);
        Assert.Contains("class=\"tl-detail-overflow-popover\"", menu);
        Assert.Contains("OnClick=\"ToggleMenu\"", menu);
        Assert.Contains("aria-expanded=\"@_isOpen\"", menu);
        Assert.Contains("class=\"tl-detail-overflow-list\"", menuItems);
        Assert.Contains("role=\"menuitem\"", menuItems);
        Assert.Contains("Disabled=\"@action.IsDisabled\"", menuItems);
        Assert.DoesNotContain("MudMenuItem", menuItems);
        Assert.Contains("action.Key is \"edit-media\" or \"edit\"", detailPage);
        Assert.DoesNotContain("OverflowActions.Concat([InlineEditAction])", actions);
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

        Assert.Contains("NormalizeSequencePlacement", source);
        Assert.Contains("NormalizeSequenceContainerOption", source);
        Assert.Contains("SourceContainerId = placement.SourceContainerId", source);
        Assert.Contains("EquivalentContainerIds = option.EquivalentContainerIds", source);
        Assert.Contains("NormalizeSequenceItem", source);
        Assert.Contains("NormalizeHeroArtwork", source);
        Assert.Contains("ImageUrl = NormalizeOptionalUrl(credit.ImageUrl)", source);
    }

    [Fact]
    public void SequencePlacementPanel_DoesNotRenderViewAllLink()
    {
        var source = ReadSource("src/MediaEngine.Web/Components/Details/SequencePlacementPanel.razor");
        var styles = ReadSource("src/MediaEngine.Web/Components/Details/DetailPage.razor.css");

        Assert.Contains("<h2>@ContainerTitleDisplay</h2>", source);
        Assert.Contains("[Parameter] public bool Compact { get; set; }", source);
        Assert.Contains("Placement.ContainerDescription", source);
        Assert.Contains("tl-series-placement--compact", source);
        Assert.Contains("@(Compact ? ContainerTitleDisplay : Placement.ContainerLabel)", source);
        Assert.Contains("item.IsCurrent && !Compact", source);
        Assert.DoesNotContain("Part of", source);
        Assert.Contains("TitleCaseDisplay(Placement.ContainerTitle, Placement.ContainerLabel)", source);
        Assert.Contains("VisibleItems", source);
        Assert.Contains("tl-series-carousel__arrow", source);
        Assert.Contains("MudChart T=\"double\"", source);
        Assert.Contains("ChartType=\"ChartType.Donut\"", source);
        Assert.Contains("ChartSeries=\"@SequenceDonutSeries\"", source);
        Assert.Contains("ChartLabels=\"@SequenceDonutLabels\"", source);
        Assert.DoesNotContain("View all", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("View Full Series", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tl-series-view-all", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tl-series-position-summary", source);
        Assert.Contains("tl-series-position-summary__donut", source);
        Assert.Contains("tl-series-position-summary__center", source);
        Assert.Contains("OnClick=\"() => SelectContainerAsync(option)\"", source);
        Assert.Contains("DistinctAvailableContainers", source);
        Assert.Contains("CanChooseDistinctContainer", source);
        Assert.Contains("NormalizeContainerOptionTitle", source);
        Assert.Contains("IsGenericContainerWord", source);
        Assert.Contains("=> OnContainerSelected.InvokeAsync(option)", source);
        Assert.DoesNotContain("Disabled=\"@option.IsSelected\"", source);
        Assert.DoesNotContain("Current position", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tl-series-item__current-badge", source);
        Assert.Contains("tl-series-item__node", source);
        Assert.Contains("SequenceItemRoute(item)", source);
        Assert.Contains("href=\"@route\"", source);
        Assert.Contains("Placement.TotalKnownItems", source);
        Assert.Contains("SequenceNumberOrNull", source);
        Assert.Contains("owned of {TotalItems}", source);
        Assert.Contains("tl-series-placement--long", source);
        Assert.Contains("LongSequenceThreshold = 9", source);
        Assert.Contains("WindowSize = 6", source);
        Assert.Contains("SequenceItemTitleClass", source);
        Assert.Contains("is-very-long", source);
        Assert.DoesNotContain("tl-series-item__owned-badge", source);
        Assert.DoesNotContain("Icons.Material.Filled.Check\" Size=\"Size.Small\"", source);
        Assert.Contains("ItemNoun", source);
        Assert.Contains("grid-template-columns: repeat(var(--series-count, 6), minmax(5.8rem, 7.3rem))", styles);
        Assert.Contains("grid-template-columns: repeat(var(--series-count, 7), minmax(7.2rem, 9.2rem))", styles);
        Assert.Contains("scrollbar-width: none", styles);
        Assert.Contains("tl-series-placement--long .tl-series-strip::before", styles);
        Assert.Contains("content: none", styles);
        Assert.Contains("min-width: clamp(5.8rem, 7.2vw, 7.3rem)", styles);
        Assert.Contains("tl-series-layout", styles);
        Assert.Contains("grid-template-columns: minmax(15rem, 0.24fr) minmax(0, 0.76fr)", styles);
        Assert.Contains("tl-series-description", styles);
        Assert.Contains("repeat(var(--series-count, 6), clamp(10.25rem, 11vw, 13rem))", styles);
        Assert.Contains("tl-series-placement:not(.tl-series-placement--compact) .tl-series-strip::before", styles);
        Assert.Contains("justify-content: start", styles);
        Assert.Contains("tl-series-wikidata-link", source);
        Assert.Contains("https://www.wikidata.org/wiki/{qid}", source);
        Assert.Contains("Series metadata: Wikidata", source);
        Assert.DoesNotContain("tl-series-owned-chip\"", source);
        Assert.Contains("tl-series-item.is-current .tl-series-item__art", styles);
        Assert.Contains("tl-series-item__current-badge", styles);
        Assert.DoesNotContain("tl-series-item__owned-badge", styles);
        Assert.Contains("tl-series-owned-summary", styles);
        Assert.Contains("tl-source-links", styles);
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
    public void RoutePages_KeepDetailTabsInPageState()
    {
        var book = ReadSource("src/MediaEngine.Web/Components/Pages/BookDetail.razor");
        var unified = ReadSource("src/MediaEngine.Web/Components/Pages/UnifiedDetailPage.razor");
        var movie = ReadSource("src/MediaEngine.Web/Components/Pages/WatchMoviePage.razor");
        var show = ReadSource("src/MediaEngine.Web/Components/Pages/WatchTvShowPage.razor");
        var episode = ReadSource("src/MediaEngine.Web/Components/Pages/WatchTvEpisodePage.razor");
        var routePages = new[] { book, unified, movie, show, episode };

        Assert.Contains("@page \"/book/{Id:guid}\"", book);
        Assert.Contains("@page \"/details/{EntityType}/{Id:guid}\"", unified);
        Assert.Contains("@page \"/watch/movie/{WorkId:guid}\"", movie);
        Assert.Contains("@page \"/watch/tv/show/{CollectionId:guid}\"", show);
        Assert.Contains("@page \"/watch/tv/show/{CollectionId:guid}/episode/{WorkId:guid}\"", episode);

        foreach (var source in routePages)
        {
            Assert.Contains("_activeTab = tab;", source);
            Assert.DoesNotContain("{Tab}", source);
            Assert.DoesNotContain("BuildTabUrl", source);
            Assert.DoesNotContain("DetailTabNavigation.BuildUrl", source);
            Assert.DoesNotContain("Nav.NavigateTo(BuildTabUrl", source);
        }
    }

    [Fact]
    public void DetailComposer_UsesOnlyLocalAiTldrForHeroSummary()
    {
        var source = ReadSource("src/MediaEngine.Api/Services/Details/DetailComposerService.cs");
        var hero = ReadSource("src/MediaEngine.Web/Components/Details/DetailHero.razor");

        Assert.Contains("BuildHeroSummary(values)", source);
        Assert.Contains("GetValue(canonicalValues, \"tldr\")", source);
        Assert.DoesNotContain("BuildFallbackHeroSummary", source);
        Assert.Contains("data-ai-summary-slot=\"tldr\"", hero);
        Assert.Contains("tl-detail-hero__tagline--ai", hero);
        Assert.Contains("BuildSeriesContextLabel", hero);
        Assert.Contains("seriesTitle += \" Series\"", hero);
        Assert.Contains("{positionedItem} of {total}", hero);
    }

    [Fact]
    public void MusicAlbumDetailsUseSharedTrackList()
    {
        var detailPage = ReadSource("src/MediaEngine.Web/Components/Details/DetailPage.razor");
        var trackList = ReadSource("src/MediaEngine.Web/Components/Details/MusicTrackList.razor");
        var audioTable = ReadSource("src/MediaEngine.Web/Components/Details/AudioItemTable.razor");
        var albumRoute = ReadSource("src/MediaEngine.Web/Components/Pages/UnifiedDetailPage.razor");

        Assert.Contains("MusicTrackList", detailPage);
        Assert.Contains("<AudioItemTable", trackList);
        Assert.Contains("ReplaceQueueItemsAsync", audioTable);
        Assert.Contains("<table class=\"tl-detail-track-table tl-audio-item-table__table\"", audioTable);
        Assert.Contains("Show missing tracks", audioTable);
        Assert.Contains("item.IsOwned", audioTable);
        Assert.Contains("DurationSeconds", audioTable);
        Assert.Contains("ToggleSort", audioTable);
        Assert.Contains("BeginColumnResize", audioTable);
        Assert.Contains("draggable=\"@item.IsOwned\"", audioTable);
        Assert.Contains("Icons.Material.Filled.Favorite", audioTable);
        Assert.DoesNotContain("@page \"/listen/album", albumRoute, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AudioDetailPagesUseCompactListenLayoutWithoutChangingHeroDefault()
    {
        var detailPage = ReadSource("src/MediaEngine.Web/Components/Details/DetailPage.razor");
        var audioLayout = ReadSource("src/MediaEngine.Web/Components/Details/AudioDetailLayout.razor");
        var chapterList = ReadSource("src/MediaEngine.Web/Components/Details/AudiobookChapterList.razor");
        var nowPlayingPanel = ReadSource("src/MediaEngine.Web/Components/Listen/ListenNowPlayingPanel.razor");
        var popupPlayer = ReadSource("src/MediaEngine.Web/Components/Pages/ListenPlayerPopupPage.razor");
        var audioTable = ReadSource("src/MediaEngine.Web/Components/Details/AudioItemTable.razor");
        var listenPage = ReadSource("src/MediaEngine.Web/Components/Pages/ListenPage.razor.cs");
        var playbackService = ReadSource("src/MediaEngine.Web/Services/Playback/PlaybackSessionController.cs");
        var playbackModels = ReadSource("src/MediaEngine.Web/Services/Playback/PlaybackModels.cs");
        var detailStyles = ReadSource("src/MediaEngine.Web/Components/Details/DetailPage.razor.css");
        var playerBar = ReadSource("src/MediaEngine.Web/Components/Listen/ListenNowPlayingBar.razor");
        var playerStyles = ReadSource("src/MediaEngine.Web/Components/Listen/ListenNowPlayingBar.razor.css");
        var transportControls = ReadSource("src/MediaEngine.Web/Components/Listen/ListenTransportControls.razor");
        var transportControlStyles = ReadSource("src/MediaEngine.Web/Components/Listen/ListenTransportControls.razor.css");
        var nowPlayingPanelStyles = ReadSource("src/MediaEngine.Web/Components/Listen/ListenNowPlayingPanel.razor.css");
        var popupPlayerStyles = ReadSource("src/MediaEngine.Web/Components/Pages/ListenPlayerPopupPage.razor.css");
        var playbackSkipButton = ReadSource("src/MediaEngine.Web/Components/Shared/PlaybackRelativeSkipButton.razor");
        var playbackSkipButtonStyles = ReadSource("src/MediaEngine.Web/Components/Shared/PlaybackRelativeSkipButton.razor.css");
        var playbackPrimaryButton = ReadSource("src/MediaEngine.Web/Components/Shared/PlaybackPrimaryButton.razor");
        var playbackPrimaryButtonStyles = ReadSource("src/MediaEngine.Web/Components/Shared/PlaybackPrimaryButton.razor.css");
        var playbackTimelineMetaRow = ReadSource("src/MediaEngine.Web/Components/Shared/PlaybackTimelineMetaRow.razor");
        var playbackTimelineMetaRowStyles = ReadSource("src/MediaEngine.Web/Components/Shared/PlaybackTimelineMetaRow.razor.css");
        var playbackSpeedControl = ReadSource("src/MediaEngine.Web/Components/Shared/PlaybackSpeedControl.razor");
        var playbackSpeedControlStyles = ReadSource("src/MediaEngine.Web/Components/Shared/PlaybackSpeedControl.razor.css");
        var playbackSleepTimerControl = ReadSource("src/MediaEngine.Web/Components/Shared/PlaybackSleepTimerControl.razor");
        var playbackSleepTimerControlStyles = ReadSource("src/MediaEngine.Web/Components/Shared/PlaybackSleepTimerControl.razor.css");
        var playbackRangeSlider = ReadSource("src/MediaEngine.Web/Components/Shared/PlaybackRangeSlider.razor");
        var playbackRangeSliderStyles = ReadSource("src/MediaEngine.Web/Components/Shared/PlaybackRangeSlider.razor.css");
        var playerScript = ReadSource("src/MediaEngine.Web/wwwroot/app.js");
        var audiobookChapterStyles = detailStyles[detailStyles.IndexOf("::deep .tl-audiobook-chapters", StringComparison.Ordinal)..];

        Assert.Contains("DetailEntityType.MusicAlbum or DetailEntityType.Audiobook", detailPage);
        Assert.Contains("<AudioDetailLayout", detailPage);
        Assert.Contains("<DetailHero Model=\"Model\"", detailPage);
        Assert.Contains("<MusicTrackList", audioLayout);
        Assert.Contains("<AudiobookChapterList", audioLayout);
        Assert.DoesNotContain("<AudioItemTable", chapterList);
        Assert.Contains("tl-audiobook-chapters__row", chapterList);
        Assert.Contains("private sealed record ChapterRow", chapterList);
        Assert.Contains("ProgressLabelFor", chapterList);
        Assert.Contains("\"Started\"", chapterList);
        Assert.Contains("% listened", chapterList);
        Assert.Contains("tl-audiobook-chapters__equalizer", chapterList);
        Assert.DoesNotContain("Icons.Material.Outlined.GraphicEq", chapterList);
        Assert.DoesNotContain("Math.Round(item.ProgressPercent.Value)", chapterList);
        Assert.DoesNotContain("tl-audiobook-chapters__progress", chapterList);
        Assert.DoesNotContain("transform: translateY", audiobookChapterStyles);
        Assert.Contains("grid-template-columns: minmax(0, 1fr) 8.25rem 5.4rem 3rem", detailStyles);
        Assert.Contains("PlayAudiobookChapterAsync", chapterList);
        Assert.Contains("ChapterDuration", nowPlayingPanel);
        Assert.Contains("chapter.EndSeconds.Value - chapter.StartSeconds", nowPlayingPanel);
        Assert.Contains("<ListenTransportControls", nowPlayingPanel);
        Assert.Contains("<PlaybackPrimaryButton", transportControls);
        Assert.DoesNotContain("PlayButtonStyle", transportControls);
        Assert.DoesNotContain("PlayIconClass", transportControls);
        Assert.DoesNotContain("Icons.Material.Filled.PlayArrow", transportControls);
        Assert.Contains("[Parameter(CaptureUnmatchedValues = true)]", playbackPrimaryButton);
        Assert.Contains("@attributes=\"AdditionalAttributes\"", playbackPrimaryButton);
        Assert.Contains("playback-primary-button-shell--compact", playbackPrimaryButtonStyles);
        Assert.Contains("playback-primary-button-shell--large", playbackPrimaryButtonStyles);
        Assert.Contains("--playback-primary-size: 54px;", playbackPrimaryButtonStyles);
        Assert.Contains("--playback-primary-size: 58px;", playbackPrimaryButtonStyles);
        Assert.Contains("--playback-primary-size: 64px;", playbackPrimaryButtonStyles);
        Assert.DoesNotContain("--playback-primary-size: 74px;", playbackPrimaryButtonStyles);
        Assert.DoesNotContain("--playback-primary-size: 96px;", playbackPrimaryButtonStyles);
        Assert.Contains("\"popup\" => \"large\"", transportControls);
        Assert.Contains("\"panel\" => \"standard\"", transportControls);
        Assert.Contains("_ => \"compact\"", transportControls);
        Assert.Contains("listen-transport__secondary-icon", transportControls);
        Assert.Contains("--listen-transport-secondary-size: 46px;", transportControlStyles);
        Assert.Contains("--listen-transport-primary-size: 54px;", transportControlStyles);
        Assert.Contains("--listen-transport-secondary-size: 50px;", transportControlStyles);
        Assert.Contains("--listen-transport-primary-size: 58px;", transportControlStyles);
        Assert.Contains("--listen-transport-secondary-size: 54px;", transportControlStyles);
        Assert.Contains("--listen-transport-primary-size: 64px;", transportControlStyles);
        Assert.Contains("grid-template-columns: var(--listen-transport-secondary-size) var(--listen-transport-secondary-size) var(--listen-transport-primary-size) var(--listen-transport-secondary-size) var(--listen-transport-secondary-size) !important;", transportControlStyles);
        Assert.Contains("width: var(--listen-transport-secondary-size) !important;", transportControlStyles);
        Assert.Contains("--playback-relative-skip-size: var(--listen-transport-secondary-size);", transportControlStyles);
        Assert.Contains("grid-template-columns: 46px 46px 54px 46px 46px;", playerStyles);
        Assert.Contains("grid-template-columns: var(--listen-skip-button-size) var(--listen-skip-button-size) var(--listen-transport-primary-size) var(--listen-skip-button-size) var(--listen-skip-button-size);", nowPlayingPanelStyles);
        Assert.Contains("grid-template-columns: 54px 54px 64px 54px 54px;", popupPlayerStyles);
        Assert.DoesNotContain("grid-template-columns: 38px 54px 52px 54px 38px", playerStyles);
        Assert.DoesNotContain("grid-template-columns: 34px var(--listen-skip-button-size) 74px", nowPlayingPanelStyles);
        Assert.DoesNotContain("grid-template-columns: 72px 64px 104px 64px 72px", popupPlayerStyles);
        Assert.Contains("<PlaybackTimelineMetaRow", nowPlayingPanel);
        Assert.Contains("<PlaybackTimelineMetaRow", popupPlayer);
        Assert.Contains("playback-timeline-meta-row", playbackTimelineMetaRow);
        Assert.Contains("color: rgba(248, 250, 252, 0.94);", playbackTimelineMetaRowStyles);
        Assert.DoesNotContain(".listen-now-panel__chapter-row", nowPlayingPanelStyles);
        Assert.DoesNotContain(".listen-popup__chapter-row", popupPlayerStyles);
        Assert.DoesNotContain("background: var(--listen-accent", transportControlStyles);
        Assert.DoesNotContain("background: var(--listen-audio-accent", transportControlStyles);
        Assert.DoesNotContain(".listen-player__play {", playerStyles);
        Assert.DoesNotContain(".listen-now-panel__play {", nowPlayingPanelStyles);
        Assert.DoesNotContain(".listen-popup__play {", popupPlayerStyles);
        Assert.Contains("<PlaybackRelativeSkipButton", transportControls);
        Assert.Contains("playback-relative-skip", playbackSkipButton);
        Assert.Contains("viewBox=\"0 0 56 56\"", playbackSkipButton);
        Assert.Contains("--playback-relative-skip-size: 46px;", playbackSkipButtonStyles);
        Assert.Contains("--playback-relative-skip-size: 50px;", playbackSkipButtonStyles);
        Assert.Contains("--playback-relative-skip-size: 54px;", playbackSkipButtonStyles);
        Assert.Contains("playback-relative-skip__glyph", playbackSkipButton);
        Assert.Contains("playback-relative-skip__arc", playbackSkipButton);
        Assert.Contains("playback-relative-skip__arrow", playbackSkipButton);
        Assert.Contains("<text class=\"@NumberClass\"", playbackSkipButton);
        Assert.Contains("data-playback-seek-delta", playbackSkipButton);
        Assert.Contains("M47 29A19 19 0 1 1 28 9", playbackSkipButton);
        Assert.Contains("M9 29A19 19 0 1 0 28 9", playbackSkipButton);
        Assert.DoesNotContain("translate(56 0) scale(-1 1)", playbackSkipButton);
        Assert.Contains("consumeImmediateToggleHandled", nowPlayingPanel);
        Assert.Contains("consumeImmediateSeekHandled", nowPlayingPanel);
        Assert.DoesNotContain("AudiobookSkipButton", transportControls + playbackSkipButton + playbackSkipButtonStyles);
        Assert.DoesNotContain("audiobook-skip-button", transportControls + playbackSkipButton + playbackSkipButtonStyles);
        Assert.DoesNotContain("audiobook-skip-button__line", transportControls + playbackSkipButton + playbackSkipButtonStyles);
        Assert.DoesNotContain("audiobook-skip-button__unit", transportControls + playbackSkipButton + playbackSkipButtonStyles);
        Assert.DoesNotContain("data-listen-seek-delta", transportControls + playbackSkipButton + playbackSkipButtonStyles);
        Assert.DoesNotContain("Icons.Material.Filled.Replay30", transportControls);
        Assert.DoesNotContain("Icons.Material.Filled.Forward30", transportControls);
        Assert.DoesNotContain("audiobook-skip-button__icon", transportControls);
        Assert.DoesNotContain("listen-skip-glyph", nowPlayingPanel);
        Assert.DoesNotContain("<strong>@value</strong>", nowPlayingPanel);
        Assert.DoesNotContain("<span>@label</span>", nowPlayingPanel);
        Assert.DoesNotContain("title=\"@label\"", transportControls + playbackSkipButton);
        Assert.Contains("@if (!Playback.IsAudiobookMode)", nowPlayingPanel);
        Assert.DoesNotContain("<h2>@current.Title</h2>\n                <p>@PlayerSubtitle(current)</p>", nowPlayingPanel.Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.Contains("HistorySubtitle(item)", nowPlayingPanel);
        Assert.Contains("HistoryPositionText(item)", nowPlayingPanel);
        Assert.Contains("<PlaybackPositionRow", nowPlayingPanel);
        Assert.Contains("<PlaybackPositionList", nowPlayingPanel);
        Assert.Contains("<PlaybackSpeedControl", nowPlayingPanel);
        Assert.Contains("<PlaybackSleepTimerControl", nowPlayingPanel);
        Assert.Contains("Adjust playback speed", nowPlayingPanel);
        Assert.DoesNotContain("SpeedRates", nowPlayingPanel);
        Assert.DoesNotContain("Choose playback speed", nowPlayingPanel);
        Assert.DoesNotContain("listen-now-panel__radio", nowPlayingPanel + nowPlayingPanelStyles);
        Assert.DoesNotContain("TimerRow(", nowPlayingPanel);
        Assert.DoesNotContain("SleepTimerDisplay", nowPlayingPanel);
        Assert.DoesNotContain("sheet-row--timer", nowPlayingPanel + nowPlayingPanelStyles);
        Assert.Contains("BookmarkPositionText(bookmark)", nowPlayingPanel);
        Assert.Contains("BookmarkSubtitle(bookmark)", nowPlayingPanel);
        Assert.Contains("Presentation=\"list\"", nowPlayingPanel);
        Assert.Contains("Kind=\"add\"", nowPlayingPanel);
        Assert.DoesNotContain("listen-now-panel__history-index", nowPlayingPanel);
        Assert.DoesNotContain("listen-now-panel__sheet-primary", nowPlayingPanel);
        Assert.DoesNotContain("SecondaryActionLabel=\"Delete bookmark\"", nowPlayingPanel);
        Assert.Contains("CurrentChapterProgressLabel", nowPlayingPanel);
        Assert.Contains("<PlaybackControlStrip", nowPlayingPanel);
        Assert.Contains("<PlaybackToolSheet", nowPlayingPanel);
        Assert.DoesNotContain("listen-now-panel__sheet-close", nowPlayingPanel);
        Assert.DoesNotContain("AudiobookSpeedActionButton", nowPlayingPanel);
        Assert.DoesNotContain("AudiobookActionButton", nowPlayingPanel);
        Assert.Contains("ChapterDuration", popupPlayer);
        Assert.Contains("chapter.EndSeconds.Value - chapter.StartSeconds", popupPlayer);
        Assert.Contains("playback-relative-skip", playbackSkipButton);
        Assert.Contains("playback-relative-skip__arc", playbackSkipButton);
        Assert.Contains("playback-relative-skip__arrow", playbackSkipButton);
        Assert.Contains("playback-relative-skip__number", playbackSkipButton);
        Assert.Contains("data-playback-seek-delta", playbackSkipButton);
        Assert.Contains("consumeImmediateToggleHandled", popupPlayer);
        Assert.Contains("consumeImmediateSeekHandled", popupPlayer);
        Assert.DoesNotContain("Icons.Material.Filled.Replay30", popupPlayer);
        Assert.DoesNotContain("Icons.Material.Filled.Forward30", popupPlayer);
        Assert.DoesNotContain("audiobook-skip-button__icon", popupPlayer);
        Assert.DoesNotContain("listen-skip-glyph", popupPlayer);
        Assert.DoesNotContain("<strong>@value</strong>", popupPlayer);
        Assert.DoesNotContain("<span>@label</span>", popupPlayer);
        Assert.DoesNotContain("title=\"@label\"", transportControls + playbackSkipButton);
        Assert.Contains("@if (!IsAudiobookMode)", popupPlayer);
        Assert.DoesNotContain("<h1>@current.Title</h1>\n                <p>@PlayerSubtitle(current)</p>", popupPlayer.Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.Contains("HistorySubtitle(item)", popupPlayer);
        Assert.Contains("HistoryPositionText(item)", popupPlayer);
        Assert.Contains("<PlaybackPositionRow", popupPlayer);
        Assert.Contains("<PlaybackPositionList", popupPlayer);
        Assert.Contains("<PlaybackSpeedControl", popupPlayer);
        Assert.Contains("<PlaybackSleepTimerControl", popupPlayer);
        Assert.Contains("Adjust playback speed", popupPlayer);
        Assert.DoesNotContain("SpeedRates", popupPlayer);
        Assert.DoesNotContain("Choose playback speed", popupPlayer);
        Assert.DoesNotContain("listen-popup-sheet__speed-row", popupPlayer + popupPlayerStyles);
        Assert.DoesNotContain("listen-popup-sheet__radio", popupPlayer + popupPlayerStyles);
        Assert.DoesNotContain("TimerRow(", popupPlayer);
        Assert.DoesNotContain("SleepTimerDisplay", popupPlayer);
        Assert.DoesNotContain("listen-popup-sheet__row--timer", popupPlayer + popupPlayerStyles);
        Assert.Contains("<PlaybackRangeSlider", playbackSpeedControl);
        Assert.Contains("<PlaybackRangeSlider", playbackSleepTimerControl);
        Assert.Contains("[Parameter(CaptureUnmatchedValues = true)]", playbackSpeedControl);
        Assert.Contains("@attributes=\"AdditionalAttributes\"", playbackSpeedControl);
        Assert.Contains("type=\"range\"", playbackRangeSlider);
        Assert.Contains("@oninput=\"HandleInputAsync\"", playbackRangeSlider);
        Assert.Contains("_interactiveValue = snapped", playbackRangeSlider);
        Assert.Contains("ResolvedInputStep", playbackRangeSlider);
        Assert.Contains("ResolvedInputStepText", playbackRangeSlider);
        Assert.Contains("SnapValueToStep", playbackRangeSlider);
        Assert.Contains("SelectTickAsync", playbackRangeSlider);
        Assert.Contains("TickLabelClass", playbackRangeSlider);
        Assert.Contains("TickButtonAriaLabel", playbackRangeSlider);
        Assert.Contains("<AppNativeButton Type=\"button\"", playbackRangeSlider);
        Assert.Contains("playback-range-slider__visual", playbackRangeSlider);
        Assert.Contains("playback-range-slider__track", playbackRangeSlider);
        Assert.Contains("playback-range-slider__fill", playbackRangeSlider);
        Assert.Contains("playback-range-slider__thumb", playbackRangeSlider);
        Assert.Contains("<div class=\"@RootClass\" style=\"@TrackStyle\">", playbackRangeSlider);
        Assert.Contains("Quick presets", playbackSpeedControl);
        Assert.Contains("Fine adjustment", playbackSpeedControl);
        Assert.Contains("Reset to @FormatSpeed(ResetValue)", playbackSpeedControl);
        Assert.Contains("Current timer", playbackSleepTimerControl);
        Assert.Contains("Step=\"@SliderStepMinutes\"", playbackSleepTimerControl);
        Assert.Contains("InputStep=\"@SliderStepMinutes\"", playbackSleepTimerControl);
        Assert.Contains("SnapValueToStep=\"@(!IsTimerActive)\"", playbackSleepTimerControl);
        Assert.Contains("MajorPresetMinutes", playbackSleepTimerControl);
        Assert.Contains("SliderMaxMinutes", playbackSleepTimerControl);
        Assert.Contains("ClampToTimerStep", playbackSleepTimerControl);
        Assert.Contains("SleepTimerStop", playbackSleepTimerControl);
        Assert.DoesNotContain("Off\\n0 min", playbackSleepTimerControl);
        Assert.DoesNotContain("playback-sleep-timer__step-label", playbackSleepTimerControl + playbackSleepTimerControlStyles);
        Assert.DoesNotContain("Adjust in @FineStepMinutes-minute steps", playbackSleepTimerControl);
        Assert.Contains("System.Threading.Timer? _refreshTimer", playbackSleepTimerControl);
        Assert.Contains("TimeSpan.FromSeconds(15)", playbackSleepTimerControl);
        Assert.Contains("playback-speed-control__presets", playbackSpeedControlStyles);
        Assert.Contains("playback-speed-control__stepper", playbackSpeedControlStyles);
        Assert.Contains("_pendingValue", playbackSpeedControl);
        Assert.Contains("playback-sleep-timer__presets", playbackSleepTimerControlStyles);
        Assert.Contains("playback-sleep-timer__stepper", playbackSleepTimerControlStyles);
        Assert.Contains("playback-sleep-timer__chapter", playbackSleepTimerControlStyles);
        Assert.Contains("playback-sleep-timer__step--decrease", playbackSleepTimerControl);
        Assert.Contains("playback-sleep-timer__step--increase", playbackSleepTimerControl);
        Assert.Contains("grid-template-columns: repeat(5, minmax(0, 1fr));", playbackSleepTimerControlStyles);
        Assert.Contains("grid-template-columns: repeat(2, minmax(0, 1fr));", playbackSleepTimerControlStyles);
        Assert.Contains("white-space: nowrap;", playbackSleepTimerControlStyles);
        Assert.DoesNotContain("grid-template-columns: repeat(auto-fit, minmax(82px, 1fr));", playbackSleepTimerControlStyles);
        Assert.Contains("playback-range-slider__input", playbackRangeSliderStyles);
        Assert.Contains("--playback-range-percent", playbackRangeSliderStyles);
        Assert.Contains("--playback-range-track-inset", playbackRangeSliderStyles);
        Assert.Contains("playback-range-slider__visual", playbackRangeSliderStyles);
        Assert.Contains("playback-range-slider__track", playbackRangeSliderStyles);
        Assert.Contains("playback-range-slider__fill", playbackRangeSliderStyles);
        Assert.Contains("playback-range-slider__thumb", playbackRangeSliderStyles);
        Assert.Contains("opacity: 0;", playbackRangeSliderStyles);
        Assert.Contains("inset: 0 var(--playback-range-track-inset);", playbackRangeSliderStyles);
        Assert.Contains("margin: 0 var(--playback-range-track-inset);", playbackRangeSliderStyles);
        Assert.Contains(".playback-range-slider ::deep .playback-range-slider__label", playbackRangeSliderStyles);
        Assert.Contains("position: absolute;", playbackRangeSliderStyles);
        Assert.Contains("top: 0;", playbackRangeSliderStyles);
        Assert.Contains("BookmarkPositionText(bookmark)", popupPlayer);
        Assert.Contains("BookmarkSubtitle(bookmark)", popupPlayer);
        Assert.Contains("Presentation=\"list\"", popupPlayer);
        Assert.Contains("Kind=\"add\"", popupPlayer);
        Assert.DoesNotContain("playback-sheet-row__index", popupPlayer);
        Assert.DoesNotContain("listen-popup-sheet__index", popupPlayer);
        Assert.DoesNotContain("Variant=\"history\"", popupPlayer);
        Assert.DoesNotContain("listen-popup-sheet__primary", popupPlayer);
        Assert.DoesNotContain("SecondaryActionLabel=\"Delete bookmark\"", popupPlayer);
        Assert.Contains("CurrentChapterProgressLabel", popupPlayer);
        Assert.DoesNotContain("FormatChapterTimeRange", chapterList);
        Assert.Contains("SupplementalActiveTab is \"chapters\"", audioLayout);
        Assert.Contains("tl-audio-detail__hero-artwash", audioLayout);
        Assert.Contains("tl-audio-detail__metadata", audioLayout);
        Assert.Contains("tl-audio-detail__hero--title-dense", audioLayout);
        Assert.DoesNotContain("HeroMetadataPills", audioLayout);
        Assert.DoesNotContain("<AudiobookChapterList Groups=\"Model.MediaGroups\"", audioLayout[..audioLayout.IndexOf("<DetailTabs", StringComparison.Ordinal)]);
        Assert.Contains("InitialPositionSeconds = ResumePositionFor(item)", audioTable);
        Assert.Contains("ResolveAudiobookTotalSeconds(chapters)", detailPage);
        Assert.Contains("ResolveAudiobookResumePosition(chapters, Model.Progress?.Percent, Playback.AudiobookNearStartGuardSeconds, totalDurationSeconds)", detailPage);
        Assert.Contains("\"data-listen-start-progress\"", detailPage);
        Assert.Contains("\"data-listen-start-duration\"", detailPage);
        Assert.Contains("\"data-listen-start-rewind\"", detailPage);
        Assert.Contains("Playback.ResumeRewindSeconds", detailPage);
        Assert.Contains("item.ResumePositionSeconds is > 0", detailPage);
        Assert.Contains("StartAudiobookAsync", detailPage);
        Assert.Contains("AudiobookStartKinds.Resume", detailPage);
        Assert.Contains("ResolveAudiobookResumeItem(chapters, resumePositionSeconds)", detailPage);
        Assert.Contains("InitialPositionSeconds = initialPositionSeconds ?? ResumePositionFor(item)", detailPage);
        Assert.Contains("!IsCompletedChapter(item) && !IsIntroChapter(item)", detailPage);
        Assert.Contains("item.ResumePositionSeconds is > 0", audioTable);
        Assert.Contains("Chapters = chapters.Select(ToPlaybackChapter).ToList()", detailPage);
        Assert.Contains("Chapters = IsAudiobook ? VisibleItems.Select(ToPlaybackChapter).ToList()", audioTable);
        Assert.Contains("BootstrapDirectStream", playbackService);
        Assert.Contains("public int ResumeRewindSeconds", playbackService);
        Assert.Contains("RequestTransportCommandAsync(CreateStartCommand())", playbackService);
        Assert.Contains("public sealed record PlaybackTransportCommand(", playbackModels);
        Assert.Contains("string? StreamUrl = null", playbackModels);
        Assert.Contains("<audio @ref=\"_audioRef\"", playerBar);
        Assert.DoesNotContain("@key=\"Playback.CurrentBrowserStreamUrl\"", playerBar);
        Assert.DoesNotContain("autoplay", playerBar);
        Assert.Contains("StartAudioAsync", playerBar);
        Assert.Contains("listenPlayback.startAudio", playerBar);
        Assert.Contains("id=\"listen-audio-engine\"", playerBar);
        Assert.Contains("listenPlayback.ensureAudioSource", playerBar);
        Assert.DoesNotContain("listenPlayback.loadAudio", playerBar);
        Assert.DoesNotContain("Math.Abs(_lastSeekedPositionSeconds.Value - targetPositionSeconds)", playerBar);
        Assert.DoesNotContain("@ontimeupdate=\"HandleAudioMetricsChangedAsync\"", playerBar);
        Assert.Contains("startAudio: startAudioElement", playerScript);
        Assert.Contains("ensureAudioSource: ensureAudioSource", playerScript);
        Assert.Contains("consumeImmediateToggleHandled", playerScript);
        Assert.Contains("consumeImmediateSeekHandled", playerScript);
        Assert.Contains("data-playback-seek-delta", playerScript);
        Assert.DoesNotContain("data-listen-seek-delta", playerScript);
        Assert.Contains("data-listen-immediate-start", playerScript);
        Assert.Contains("startPositionFromDataset", playerScript);
        Assert.Contains("listenPendingStartPosition", playerScript);
        Assert.Contains("document.addEventListener('click'", playerScript);
        Assert.DoesNotContain("document.addEventListener('pointerdown'", playerScript);
        Assert.Contains("openPopupFromImmediateAction", playerScript);
        Assert.Contains("data-listen-popup-route", playerBar);
        Assert.Contains("AudiobookActionAttributes", detailPage);
        Assert.Contains("ActionAttributes=\"AudiobookActionAttributes\"", detailPage);
        Assert.Contains("registerAudioStateObserver: function", playerScript);
        Assert.Contains("grid-template-columns: 4.35rem minmax(0, 1fr) 4.35rem", playerStyles);
        Assert.Contains("width: min(100%, 720px);", playerStyles);
        Assert.Contains("FormatSeconds(item.DurationSeconds.Value, forceHours: IsAudiobook)", audioTable);
        Assert.Contains("FormatChapterTimeRange(item.StartSeconds.Value, item.EndSeconds)", audioTable);
        Assert.Contains("Playback.CurrentTimeSeconds", audioTable);
        Assert.Contains("currentItem.AssetId.Value != assetId", audioTable);
        Assert.Contains("GetDetailPageAsync(", listenPage);
        Assert.Contains("DetailEntityType.Audiobook", listenPage);
        Assert.DoesNotContain("NavigateTo($\"/book/{WorkId.Value}?mode=listen\"", listenPage, StringComparison.Ordinal);
    }

    [Fact]
    public void AudioDetailActionsAndTablesUseHeartFavoritesAndAlignedHeroIcons()
    {
        var actions = ReadSource("src/MediaEngine.Web/Components/Details/HeroActionRow.razor");
        var styles = ReadSource("src/MediaEngine.Web/Components/Details/DetailPage.razor.css");
        var songTable = ReadSource("src/MediaEngine.Web/Components/Listen/ListenSongTable.razor");
        var trackGrid = ReadSource("src/MediaEngine.Web/Components/Listen/ListenTrackDataGrid.razor");
        var libraryTable = ReadSource("src/MediaEngine.Web/Components/Library/LibraryConfigurableTable.razor");

        Assert.Contains("else if (IsPrimaryHeroActionRow)", actions);
        Assert.Contains("tl-detail-watch-secondary--icon", actions);
        Assert.Contains("border-radius: 999px", styles);
        Assert.Contains("Icons.Material.Filled.Favorite", songTable);
        Assert.Contains("Icons.Material.Outlined.FavoriteBorder", songTable);
        Assert.Contains("Icons.Material.Filled.Favorite", trackGrid);
        Assert.Contains("Icons.Material.Outlined.FavoriteBorder", trackGrid);
        Assert.Contains("Icons.Material.Filled.Favorite", libraryTable);
        Assert.Contains("Icons.Material.Outlined.FavoriteBorder", libraryTable);
        Assert.DoesNotContain("Icons.Material.Filled.Star", songTable);
        Assert.DoesNotContain("Icons.Material.Outlined.StarBorder", trackGrid);
    }

    [Fact]
    public void PlaybackPopoutUsesGenericShellAndAccessibleActions()
    {
        var popup = ReadSource("src/MediaEngine.Web/Components/Pages/ListenPlayerPopupPage.razor");
        var popupCss = ReadSource("src/MediaEngine.Web/Components/Pages/ListenPlayerPopupPage.razor.css");
        var host = ReadSource("src/MediaEngine.Web/Components/Listen/ListenNowPlayingBar.razor");
        var hostCss = ReadSource("src/MediaEngine.Web/Components/Listen/ListenNowPlayingBar.razor.css");
        var transportControls = ReadSource("src/MediaEngine.Web/Components/Listen/ListenTransportControls.razor");
        var controlStrip = ReadSource("src/MediaEngine.Web/Components/Shared/PlaybackControlStrip.razor");
        var controlStripCss = ReadSource("src/MediaEngine.Web/Components/Shared/PlaybackControlStrip.razor.css");
        var iconButtonCss = ReadSource("src/MediaEngine.Web/Components/Shared/PlaybackIconButton.razor.css");
        var toolSheet = ReadSource("src/MediaEngine.Web/Components/Shared/PlaybackToolSheet.razor");
        var toolSheetCss = ReadSource("src/MediaEngine.Web/Components/Shared/PlaybackToolSheet.razor.css");
        var sheetHandle = ReadSource("src/MediaEngine.Web/Components/Shared/PlaybackSheetHandleButton.razor");
        var sheetHandleCss = ReadSource("src/MediaEngine.Web/Components/Shared/PlaybackSheetHandleButton.razor.css");
        var popoutShell = ReadSource("src/MediaEngine.Web/Components/Shared/PlaybackPopoutShell.razor");
        var miniPlayer = ReadSource("src/MediaEngine.Web/Components/Shared/PlaybackMiniPlayer.razor");
        var valueToolButton = ReadSource("src/MediaEngine.Web/Components/Shared/PlaybackValueToolButton.razor");
        var sheetList = ReadSource("src/MediaEngine.Web/Components/Shared/PlaybackSheetList.razor");
        var sheetListCss = ReadSource("src/MediaEngine.Web/Components/Shared/PlaybackSheetList.razor.css");
        var sheetRow = ReadSource("src/MediaEngine.Web/Components/Shared/PlaybackSheetRow.razor");
        var sheetRowCss = ReadSource("src/MediaEngine.Web/Components/Shared/PlaybackSheetRow.razor.css");
        var positionRow = ReadSource("src/MediaEngine.Web/Components/Shared/PlaybackPositionRow.razor");
        var positionRowCss = ReadSource("src/MediaEngine.Web/Components/Shared/PlaybackPositionRow.razor.css");
        var positionList = ReadSource("src/MediaEngine.Web/Components/Shared/PlaybackPositionList.razor");
        var positionListCss = ReadSource("src/MediaEngine.Web/Components/Shared/PlaybackPositionList.razor.css");
        var speedControl = ReadSource("src/MediaEngine.Web/Components/Shared/PlaybackSpeedControl.razor");
        var speedControlCss = ReadSource("src/MediaEngine.Web/Components/Shared/PlaybackSpeedControl.razor.css");
        var sleepTimerControl = ReadSource("src/MediaEngine.Web/Components/Shared/PlaybackSleepTimerControl.razor");
        var sleepTimerControlCss = ReadSource("src/MediaEngine.Web/Components/Shared/PlaybackSleepTimerControl.razor.css");
        var rangeSlider = ReadSource("src/MediaEngine.Web/Components/Shared/PlaybackRangeSlider.razor");
        var rangeSliderCss = ReadSource("src/MediaEngine.Web/Components/Shared/PlaybackRangeSlider.razor.css");

        Assert.Contains("listen-popup__stage", popup);
        Assert.Contains("<PlaybackPopoutShell", popup);
        Assert.Contains("<PlaybackControlStrip", popup);
        Assert.Contains("<PlaybackToolSheet", popup);
        Assert.Contains("Presentation=\"full-overlay\"", popup);
        Assert.Contains("<PlaybackSheetList", popup);
        Assert.Contains("<PlaybackSheetRow", popup);
        Assert.Contains("<PlaybackPositionRow", popup);
        Assert.Contains("<PlaybackPositionList", popup);
        Assert.Contains("<PlaybackSpeedControl", popup);
        Assert.Contains("<PlaybackSleepTimerControl", popup);
        Assert.Contains("<PlaybackRangeSlider", speedControl);
        Assert.Contains("<PlaybackRangeSlider", sleepTimerControl);
        Assert.Contains("InputStep=\"@Step\"", speedControl);
        Assert.Contains("TickStep=\"0.5\"", speedControl);
        Assert.DoesNotContain("TickStep=\"0.1\"", speedControl);
        Assert.Contains("TickFormatter=\"FormatSpeedTick\"", speedControl);
        Assert.Contains("FormatSpeedTick", speedControl);
        Assert.Contains("[Parameter(CaptureUnmatchedValues = true)]", speedControl);
        Assert.Contains("@attributes=\"AdditionalAttributes\"", speedControl);
        Assert.Contains("playback-popout-shell", popoutShell);
        Assert.Contains("playback-mini-player", miniPlayer);
        Assert.Contains("playback-value-tool-button", valueToolButton);
        Assert.Contains("playback-position-row", positionRow);
        Assert.Contains("[Parameter(CaptureUnmatchedValues = true)]", positionRow);
        Assert.Contains("@attributes=\"AdditionalAttributes\"", positionRow);
        Assert.Contains("Icons.Material.Outlined.CalendarMonth", positionRow);
        Assert.Contains("playback-position-row__action-ring", positionRowCss);
        Assert.Contains("playback-position-row--action-plain", positionRowCss);
        Assert.Contains("playback-position-row-shell--list", positionRowCss);
        Assert.Contains("playback-position-row--list", positionRowCss);
        Assert.Contains("playback-position-row--add", positionRowCss);
        Assert.Contains("border: 1px dashed rgba(245, 158, 11, 0.42);", positionRowCss);
        Assert.Contains("border-bottom: 1px solid rgba(148, 163, 184, 0.16);", positionRowCss);
        Assert.Contains("margin-top: 0;", positionRowCss);
        Assert.Contains("var(--listen-accent, var(--listen-audio-accent, var(--tl-status-warning, #f59e0b)))", positionRowCss);
        Assert.Contains("playback-position-list", positionList);
        Assert.Contains("overflow: hidden;", positionListCss);
        Assert.Contains("Quick presets", speedControl);
        Assert.Contains("Fine adjustment", speedControl);
        Assert.Contains("Reset to @FormatSpeed(ResetValue)", speedControl);
        Assert.Contains("Current timer", sleepTimerControl);
        Assert.Contains("Step=\"@SliderStepMinutes\"", sleepTimerControl);
        Assert.Contains("InputStep=\"@SliderStepMinutes\"", sleepTimerControl);
        Assert.Contains("SnapValueToStep=\"@(!IsTimerActive)\"", sleepTimerControl);
        Assert.Contains("MajorPresetMinutes", sleepTimerControl);
        Assert.Contains("SliderMaxMinutes", sleepTimerControl);
        Assert.Contains("ClampToTimerStep", sleepTimerControl);
        Assert.Contains("SleepTimerStop", sleepTimerControl);
        Assert.DoesNotContain("Off\\n0 min", sleepTimerControl);
        Assert.DoesNotContain("playback-sleep-timer__step-label", sleepTimerControl + sleepTimerControlCss);
        Assert.DoesNotContain("Adjust in @FineStepMinutes-minute steps", sleepTimerControl);
        Assert.Contains("System.Threading.Timer? _refreshTimer", sleepTimerControl);
        Assert.Contains("TimeSpan.FromSeconds(15)", sleepTimerControl);
        Assert.Contains("EndOfSectionChanged", sleepTimerControl);
        Assert.Contains("type=\"range\"", rangeSlider);
        Assert.Contains("@oninput=\"HandleInputAsync\"", rangeSlider);
        Assert.Contains("@onchange=\"HandleInputAsync\"", rangeSlider);
        Assert.Contains("_interactiveValue = snapped", rangeSlider);
        Assert.Contains("ResolvedInputStep", rangeSlider);
        Assert.Contains("ResolvedInputStepText", rangeSlider);
        Assert.Contains("SnapValueToStep", rangeSlider);
        Assert.Contains("SelectTickAsync", rangeSlider);
        Assert.Contains("TickLabelClass", rangeSlider);
        Assert.Contains("TickButtonAriaLabel", rangeSlider);
        Assert.Contains("<AppNativeButton Type=\"button\"", rangeSlider);
        Assert.Contains("playback-range-slider__visual", rangeSlider);
        Assert.Contains("playback-range-slider__track", rangeSlider);
        Assert.Contains("playback-range-slider__fill", rangeSlider);
        Assert.Contains("playback-range-slider__thumb", rangeSlider);
        Assert.Contains("<div class=\"@RootClass\" style=\"@TrackStyle\">", rangeSlider);
        Assert.Contains("playback-speed-control__presets", speedControlCss);
        Assert.Contains("playback-speed-control__stepper", speedControlCss);
        Assert.Contains(".playback-speed-control ::deep .playback-speed-control__preset", speedControlCss);
        Assert.Contains(".playback-speed-control ::deep .playback-speed-control__step", speedControlCss);
        Assert.Contains("playback-speed-control__fine", speedControlCss);
        Assert.Contains("_pendingValue", speedControl);
        Assert.Contains("playback-sleep-timer__presets", sleepTimerControlCss);
        Assert.Contains("playback-sleep-timer__stepper", sleepTimerControlCss);
        Assert.Contains("playback-sleep-timer__chapter", sleepTimerControlCss);
        Assert.Contains(".playback-sleep-timer ::deep .playback-sleep-timer__preset", sleepTimerControlCss);
        Assert.Contains(".playback-sleep-timer ::deep .playback-sleep-timer__chapter", sleepTimerControlCss);
        Assert.Contains(".playback-sleep-timer ::deep .playback-sleep-timer__step", sleepTimerControlCss);
        Assert.Contains("playback-sleep-timer__step--decrease", sleepTimerControl);
        Assert.Contains("playback-sleep-timer__step--increase", sleepTimerControl);
        Assert.Contains("grid-template-columns: repeat(5, minmax(0, 1fr));", sleepTimerControlCss);
        Assert.Contains("grid-template-columns: repeat(2, minmax(0, 1fr));", sleepTimerControlCss);
        Assert.Contains("white-space: nowrap;", sleepTimerControlCss);
        Assert.DoesNotContain("grid-template-columns: repeat(auto-fit, minmax(82px, 1fr));", sleepTimerControlCss);
        Assert.Contains("playback-range-slider__input", rangeSliderCss);
        Assert.Contains("--playback-range-percent", rangeSliderCss);
        Assert.Contains("--playback-range-track-inset", rangeSliderCss);
        Assert.Contains("playback-range-slider__visual", rangeSliderCss);
        Assert.Contains("playback-range-slider__track", rangeSliderCss);
        Assert.Contains("playback-range-slider__fill", rangeSliderCss);
        Assert.Contains("playback-range-slider__thumb", rangeSliderCss);
        Assert.Contains("opacity: 0;", rangeSliderCss);
        Assert.Contains("inset: 0 var(--playback-range-track-inset);", rangeSliderCss);
        Assert.Contains("margin: 0 var(--playback-range-track-inset);", rangeSliderCss);
        Assert.Contains(".playback-range-slider ::deep .playback-range-slider__label", rangeSliderCss);
        Assert.Contains("position: absolute;", rangeSliderCss);
        Assert.Contains("top: 0;", rangeSliderCss);
        Assert.Contains("playback-sheet-list", sheetList);
        Assert.Contains("padding: 4px 12px 18px;", sheetListCss);
        Assert.Contains("playback-sheet-row", sheetRow);
        Assert.Contains("playback-sheet-row-shell", sheetRow);
        Assert.Contains(".playback-sheet-row-shell ::deep .playback-sheet-row", sheetRowCss);
        Assert.Contains("justify-content: space-between;", sheetRowCss);
        Assert.Contains("listen-popup__actions", popup);
        Assert.Contains("Size=\"standard\"", popup);
        Assert.Contains("grid-template-columns: repeat(5, minmax(44px, 1fr));", popupCss);
        Assert.Contains(".playback-control-strip.listen-popup__actions", controlStripCss);
        Assert.Contains("grid-template-columns: repeat(5, minmax(44px, 1fr));", controlStripCss);
        Assert.Contains("Speed", popup);
        Assert.Contains("Chapters", popup);
        Assert.Contains("History", popup);
        Assert.Contains("Bookmark", popup);
        Assert.Contains("Sleep Timer", popup);
        Assert.Contains("<PlaybackIconButton", controlStrip);
        Assert.Contains("::deep .playback-icon-button__label", iconButtonCss);
        Assert.Contains("::deep .playback-icon-button__value", iconButtonCss);
        Assert.Contains("Control.BadgeText", ReadSource("src/MediaEngine.Web/Components/Shared/PlaybackIconButton.razor"));
        Assert.Contains("playback-icon-button__badge", iconButtonCss);
        Assert.Contains("playback-icon-button--sleep-timer.is-active", iconButtonCss);
        Assert.Contains("#c084fc", iconButtonCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<PlaybackSheetHandleButton", toolSheet);
        Assert.Contains("NormalizedPresentation", toolSheet);
        Assert.Contains("full-overlay", toolSheet);
        Assert.Contains("playback-tool-sheet--full-overlay", toolSheetCss);
        Assert.Contains("playback-tool-sheet-roll-up", toolSheetCss);
        Assert.Contains("prefers-reduced-motion", toolSheetCss);
        Assert.DoesNotContain("border-bottom: 1px solid rgba(148, 163, 184, 0.16);", toolSheetCss);
        Assert.Contains("[Parameter(CaptureUnmatchedValues = true)]", sheetHandle);
        Assert.Contains("@attributes=\"AdditionalAttributes\"", sheetHandle);
        Assert.Contains("playback-sheet-handle-button-shell", sheetHandle);
        Assert.Contains(".playback-sheet-handle-button-shell ::deep .playback-sheet-handle-button", sheetHandleCss);
        Assert.Contains("align-items: flex-start;", sheetHandleCss);
        Assert.Contains("box-sizing: border-box;", sheetHandleCss);
        Assert.Contains("height: 44px;", sheetHandleCss);
        Assert.Contains("margin: 8px auto 0;", sheetHandleCss);
        Assert.Contains("padding: 4px 36px 0;", sheetHandleCss);
        Assert.Contains("listen-popup-sheet__backdrop", popup);
        Assert.Contains("OnKeyDown=\"HandlePopupKeyDown\"", popup);
        Assert.DoesNotContain("listen-popup-sheet__grabber", popup + popupCss);
        Assert.DoesNotContain("listen-popup-sheet__close", popup + popupCss);
        Assert.DoesNotContain("SpeedActionButton", popup);
        Assert.DoesNotContain("SpeedRates", popup);
        Assert.DoesNotContain("Choose playback speed", popup);
        Assert.DoesNotContain("listen-popup-sheet__speed-row", popup + popupCss);
        Assert.DoesNotContain("listen-popup-sheet__radio", popup + popupCss);
        Assert.DoesNotContain("TimerRow(", popup);
        Assert.DoesNotContain("SleepTimerDisplay", popup);
        Assert.DoesNotContain("listen-popup-sheet__row--timer", popup + popupCss);
        Assert.DoesNotContain("private RenderFragment ActionButton", popup);
        Assert.DoesNotContain("playback-sheet-row__index", popup + sheetRowCss);
        Assert.DoesNotContain("listen-popup-sheet__index", popup + popupCss);
        Assert.DoesNotContain("Variant=\"history\"", popup);
        Assert.DoesNotContain("PlaybackHistoryRow", popup + positionRow + positionRowCss);
        Assert.DoesNotContain("listen-popup-sheet__primary", popup + popupCss);
        Assert.DoesNotContain(".listen-popup__action strong", popupCss);
        Assert.DoesNotContain("display: none", popupCss);
        Assert.Contains("play-chapter", popup);
        Assert.Contains("add-audiobook-bookmark", popup);
        Assert.Contains("set-sleep-timer", popup);
        Assert.Contains("<ListenTransportControls", popup);
        Assert.Contains("<PlaybackRelativeSkipButton Seconds=\"@SkipBackSeconds\" Direction=\"back\"", transportControls);
        Assert.Contains("<PlaybackRelativeSkipButton Seconds=\"@SkipForwardSeconds\" Direction=\"forward\"", transportControls);
        Assert.Contains("listen-popup-menu", popup);
        Assert.Contains("<PlaybackControlStrip", host);
        Assert.Contains("<PlaybackMiniPlayer", host);
        Assert.Contains("Class=\"listen-player__audiobook-actions\"", host);
        Assert.Contains("AriaLabel=\"Audiobook tools\"", host);
        Assert.Contains("OnControl=\"HandleAudiobookPanelControl\"", host);
        Assert.DoesNotContain("<PlaybackValueToolButton", host);
        Assert.Contains("ActiveSheet: Playback.IsPanelOpen ? _activeAudiobookPanelTool : null", host);
        Assert.Contains("SleepTimerValueText: BottomSleepTimerValueText", host);
        Assert.Contains("Playback.TogglePanel();", host);
        Assert.Contains("ShortSleepTimerLabel", host);
        Assert.Contains("grid-template-columns: minmax(0, 260px) minmax(300px, 1fr) minmax(590px, auto);", hostCss);
        Assert.Contains(".listen-player__actions ::deep .playback-control-strip.listen-player__audiobook-actions", hostCss);
        Assert.Contains("grid-template-columns: repeat(5, minmax(52px, 1fr)) !important;", hostCss);
        Assert.Contains("width: clamp(300px, 24vw, 350px) !important;", hostCss);
        Assert.Contains("@@media (max-width: 1240px)", hostCss);
        Assert.Contains("width: min(100%, 720px);", hostCss);
        Assert.Contains("grid-template-rows: 24px 10px 12px;", hostCss);
        Assert.Contains("min-height: 52px;", hostCss);
        Assert.Contains("<PlaybackSleepTimerControl", host);
        Assert.Contains("Close playback tool", host);
        Assert.Contains("BottomPanelTitle", host);
        Assert.DoesNotContain("Class=\"listen-player-panel__audiobook-actions\"", host);
        Assert.DoesNotContain("listen-player-panel__audiobook-actions", hostCss);
        Assert.Contains("::deep .listen-player-panel__tool-row", hostCss);
        Assert.DoesNotContain("Playback.CyclePlaybackRateAsync()", host);
        Assert.DoesNotContain("Icons.Material.Outlined.MoreHoriz", host);
        Assert.DoesNotContain("BottomPanelAriaLabel", host);
        Assert.DoesNotContain("SleepTimerRow", host);
        Assert.DoesNotContain("tool-row--timer", host + hostCss);
        Assert.Contains("play-next-chapter", host);
        Assert.Contains("play-previous-chapter", host);
        Assert.Contains("ApplyPlaybackRateAsync", host);
        Assert.Contains("--listen-accent", popupCss);
        Assert.Contains("listen-popup-sheet", popupCss);
        Assert.DoesNotContain("#ff416c", popupCss, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#ff2746", popupCss, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#8b5cf6", popupCss, StringComparison.OrdinalIgnoreCase);
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
