namespace MediaEngine.Web.Tests;

public sealed class UnifiedDetailComponentTests
{
    [Fact]
    public void HeroBackdrop_DoesNotUseBlurredCoverFallback()
    {
        var source = ReadSource("src/MediaEngine.Web/Components/Details/HeroBackdrop.razor");
        var styles = ReadSource("src/MediaEngine.Web/Components/Details/DetailPage.razor.css");

        Assert.DoesNotContain("filter: blur(", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Artwork.BackdropUrl", source);
        Assert.Contains("Artwork.CoverUrl", source);
        Assert.Contains("tl-detail-media-stage--cover-only", source);
        Assert.DoesNotContain("tl-detail-media-stage--cover-only .tl-detail-media-stage__image {\r\n    filter: blur(", styles, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tl-detail-media-stage--cover-only .tl-detail-media-stage__image {\n    filter: blur(", styles, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("place-items: center end", styles);
        Assert.Contains("object-fit: contain", styles);
        Assert.Contains("opacity: 1", styles);
        Assert.DoesNotContain("opacity: 0.58", styles);
        Assert.Contains("circle at 72% 38%", styles);
        Assert.Contains("transparent 78%", styles);
        Assert.Contains("width: 100%", styles);
        Assert.Contains("tl-detail-media-stage--real-backdrop .tl-detail-media-stage__image", styles);
        Assert.Contains("object-position: center right", styles);
        Assert.Contains("min-height: clamp(38rem, 78vh, 58rem)", styles);
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
        Assert.Contains("white-space: pre-line", styles);
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
    public void EngineClient_NormalizesSeriesAndCreditImageUrls()
    {
        var source = ReadSource("src/MediaEngine.Web/Services/Integration/EngineApiClient.cs");

        Assert.Contains("NormalizeSeriesPlacement", source);
        Assert.Contains("NormalizeSeriesItem", source);
        Assert.Contains("ImageUrl = NormalizeOptionalUrl(credit.ImageUrl)", source);
    }

    [Fact]
    public void DetailComposer_UsesWikidataDescriptionForHeroSummary()
    {
        var source = ReadSource("src/MediaEngine.Api/Services/Details/DetailComposerService.cs");

        Assert.Contains("BuildHeroSummaryAsync", source);
        Assert.Contains("qid_labels", source);
        Assert.Contains("wikidata_description", source);
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
