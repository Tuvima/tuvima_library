using Bunit;
using MediaEngine.Web.Components.Discovery;
using MediaEngine.Web.Components.MediaTiles;
using MediaEngine.Web.Models.ViewDTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace MediaEngine.Web.Tests;

public sealed class MediaTileSurfaceRenderTests : TestContext
{
    public MediaTileSurfaceRenderTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }

    [Fact]
    public void MediaTile_ArtOnlyPopoverUsesCompactArtPreview()
    {
        var item = new MediaTileViewModel
        {
            Id = Guid.NewGuid(),
            Title = "The Record",
            Subtitle = "boygenius",
            Tldr = "A sharp indie record with close harmonies.",
            VibeTags = ["Wry", "Kinetic"],
            MediaKind = "Music",
            AccentColor = "#1ED760",
            Shape = MediaTileShape.Square,
            SurfaceKind = MediaTileSurfaceKind.CoverSquare,
            HoverLayout = MediaTileHoverLayout.ArtOnlyPopover,
            TileImageUrl = "/art/album.jpg",
            HoverImageUrl = "/art/album.jpg",
            NavigationUrl = "/listen/music",
            PrimaryNavigationUrl = "/listen/music",
            PrimaryActionLabel = "Open",
        };

        var cut = RenderComponent<MediaTile>(parameters => parameters.Add(component => component.Item, item));
        var css = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Web/Components/MediaTiles/MediaTile.razor.css"));
        var shelfCss = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Web/Components/MediaTiles/MediaTileShelf.razor.css"));
        var shelfRazor = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Web/Components/MediaTiles/MediaTileShelf.razor"));
        var gridRazor = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Web/Components/MediaTiles/MediaTileGrid.razor"));
        var tileRazor = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Web/Components/MediaTiles/MediaTile.razor"));
        var appCss = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Web/wwwroot/app.css"));
        var appJs = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Web/wwwroot/app.js"));

        Assert.NotEmpty(cut.FindAll(".media-tile-hover-panel.is-art-popover.is-cover-square"));
        Assert.Null(cut.Find(".media-tile-hover-panel").Closest(".media-tile-frame"));
        Assert.NotNull(cut.Find(".media-tile-hover-panel").Closest(".media-tile"));
        Assert.DoesNotContain("style=", cut.Markup);
        Assert.Contains(".media-tile.is-square { width:var(--media-tile-media-height); --media-tile-media-aspect:1 / 1; --media-tile-hover-panel-width:clamp(320px,24vw,400px);", css);
        Assert.Contains("flex:0 0 auto", css);
        Assert.Contains("aspect-ratio:var(--media-tile-media-aspect)", css);
        Assert.Contains(".media-tile-image.is-contained { object-fit:contain; padding:0; background:transparent; }", css);
        Assert.Contains(".media-tile-hover-panel { position:absolute; left:var(--media-tile-hover-left, 0px); top:var(--media-tile-hover-top, 0px);", css);
        Assert.Contains(".media-tile-hover-panel.is-visible { opacity:1; visibility:visible; pointer-events:auto;", css);
        Assert.Contains(".media-tile:not(.is-hover-js-enabled):hover .media-tile-hover-panel,", css);
        Assert.Contains(".media-tile-hover-panel.is-viewport-mounted { position:fixed;", css);
        Assert.Contains("--media-tile-hover-max-height", css);
        Assert.Contains("--media-tile-hover-art-max-height", css);
        Assert.Contains(".media-tile-hover-actions, .media-tile-icon-button, ::deep .media-tile-icon-button { pointer-events:auto; }", css);
        Assert.Contains(".media-tile-icon-button, ::deep .media-tile-icon-button { width:42px; min-width:42px; height:42px;", css);
        Assert.Contains(".media-tile-shelf:has(.media-tile.is-hover-active),", appCss);
        Assert.Contains("window.getMediaTileHoverHost()", appJs);
        Assert.Contains("panel.classList.add('is-viewport-mounted')", appJs);
        Assert.DoesNotContain("cardEl.closest('.media-tile-shelf-track')", appJs);
        Assert.Contains("window.lockMediaTileHoverRowScroll(cardEl)", appJs);
        Assert.Contains("is-hover-scroll-locked", appJs);
        Assert.Contains("panel.addEventListener('wheel', panel.__mediaTileHoverWheelBlock, { passive: false });", appJs);
        Assert.Contains("window.isVerticalMediaTileWheel = function (event)", appJs);
        Assert.Contains("if (window.isVerticalMediaTileWheel(event))", appJs);
        Assert.DoesNotContain("mostlyHorizontal", appJs);
        Assert.Contains("var stableLeft = el.__swimlaneStableScrollLeft || 0;", appJs);
        Assert.Contains("if (Math.abs(el.scrollLeft - stableLeft) > 1)", appJs);
        Assert.Contains("event.preventDefault();", appJs);
        Assert.Contains("window.registerMediaTileShelfScrollGuard", appJs);
        Assert.Contains("window.updateMediaTileShelfVisibleWidth", appJs);
        Assert.Contains("window.addEventListener('resize', onResize, { passive: true });", appJs);
        Assert.Contains("track.style.setProperty('--media-tile-shelf-visible-width'", appJs);
        Assert.Contains("track.style.setProperty('--media-tile-shelf-arrow-offset'", appJs);
        Assert.Contains("window.getSwimlaneSnapTarget", appJs);
        Assert.Contains("el.scrollTo({ left: target, behavior: 'smooth' });", appJs);
        Assert.DoesNotContain("el.scrollBy({ left: direction === 'left' ? -amount : amount, behavior: 'smooth' });", appJs);
        Assert.Contains("registerMediaTileShelfScrollGuard", shelfRazor);
        Assert.Contains("unregisterMediaTileShelfScrollGuard", shelfRazor);
        Assert.Contains("HideMediaKindBadge=\"@HideMediaKindBadges\"", shelfRazor);
        Assert.Contains("HideSourceBadge=\"@HideSourceBadges\"", shelfRazor);
        Assert.Contains("HideMediaKindBadge=\"@HideMediaKindBadges\"", gridRazor);
        Assert.Contains("HideSourceBadge=\"@HideSourceBadges\"", gridRazor);
        Assert.Contains("!HideMediaKindBadge && !ShowCollectionBanner", tileRazor);
        Assert.Contains("private bool ShowMetaRow => !string.IsNullOrWhiteSpace(HoverMetaDisplay);", tileRazor);
        Assert.DoesNotContain("media-tile-meta-icon", tileRazor);
        Assert.Contains("!HideSourceBadge && !ShowCollectionBanner", tileRazor);
        Assert.Contains("--media-tile-shelf-visible-width: 100%;", shelfCss);
        Assert.Contains("grid-template-columns: var(--media-tile-shelf-arrow-rail) minmax(0, 1fr) var(--media-tile-shelf-arrow-rail);", shelfCss);
        Assert.Contains(".media-tile-shelf-window", shelfCss);
        Assert.Contains("overflow: hidden;", shelfCss);
        Assert.Contains(".media-tile-shelf-arrow-slot", shelfCss);
        Assert.Contains("width: min(100%, var(--media-tile-shelf-visible-width));", shelfCss);
        Assert.Contains("::deep .media-tile-shelf-arrow {", shelfCss);
        Assert.Contains("position: relative;", shelfCss);
        Assert.Contains(".media-tile-shelf-track:hover ::deep .media-tile-shelf-arrow", shelfCss);
        Assert.Contains("opacity: 0.88", shelfCss);
        Assert.Contains("width: 52px !important", shelfCss);
        Assert.Contains("background: rgba(8, 12, 24, 0.82) !important", shelfCss);
        Assert.Contains("margin-left: 6px;", shelfCss);
        Assert.Contains(".media-tile-shelf-scroll.is-hover-scroll-locked", shelfCss);
        Assert.Contains(".media-tile-shelf-scroll.is-row-scroll-guarded", shelfCss);
        Assert.Contains("overflow-x: auto", shelfCss);
        Assert.Contains("overflow-y: clip", shelfCss);
        Assert.Contains("overscroll-behavior-y: auto", shelfCss);
        Assert.Contains("touch-action: pan-x pan-y", shelfCss);
        Assert.Contains("scroll-snap-type: x mandatory", shelfCss);
        Assert.Contains("--media-tile-hover-anchor-width", appJs);
        Assert.Contains("var panelLeft = cardRect.left + (cardRect.width / 2) - (panelWidth / 2);", appJs);
        Assert.Contains("panel.classList.add('is-visible');", appJs);
        Assert.Contains("hoverImage.addEventListener('load', panel.__mediaTileHoverImageLoad, { once: true });", appJs);
        Assert.Contains("window.correctMediaTileHoverViewport", appJs);
        Assert.Contains("window.scheduleMediaTileHoverViewportCorrection(cardEl);", appJs);
        Assert.Contains("window.positionMediaTileHover(cardEl);", appJs);
        Assert.Contains("}, 220);", appJs);
        Assert.Contains("hoverImage.loading = 'eager';", appJs);
        Assert.DoesNotContain("overflow:hidden auto", css);
        Assert.NotEmpty(cut.FindAll(".media-tile-hover-image.is-contained"));
        Assert.Equal(2, cut.FindAll(".media-tile-chip").Count);
        Assert.DoesNotContain("A sharp indie record with close harmonies.", cut.Markup);
        Assert.Empty(cut.FindAll(".media-tile-hover-logo"));
        Assert.NotEmpty(cut.FindAll("button[aria-label='Details']"));
        Assert.NotEmpty(cut.FindAll("button[aria-label='More actions']"));
    }

    [Fact]
    public void MediaTile_PortraitPopoverUsesAnchoredPreview()
    {
        var item = new MediaTileViewModel
        {
            Id = Guid.NewGuid(),
            Title = "The Sandman",
            Subtitle = "Neil Gaiman",
            Tldr = "A dreamlike horror comic with mythic scale.",
            Genres = ["Fantasy", "Horror"],
            MediaKind = "Comic",
            AccentColor = "#C9922E",
            Shape = MediaTileShape.Portrait,
            SurfaceKind = MediaTileSurfaceKind.CoverPortrait,
            HoverLayout = MediaTileHoverLayout.ArtOnlyPopover,
            TileImageUrl = "/art/sandman.jpg",
            HoverImageUrl = "/art/sandman.jpg",
            NavigationUrl = "/read/comics",
            DetailsNavigationUrl = "/read/comics/work/123",
            PrimaryNavigationUrl = "/reader/123",
            PrimaryActionLabel = "Read",
        };

        var cut = RenderComponent<MediaTile>(parameters => parameters.Add(component => component.Item, item));
        var css = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Web/Components/MediaTiles/MediaTile.razor.css"));

        Assert.NotEmpty(cut.FindAll(".media-tile-hover-panel.is-art-popover.is-portrait.is-cover-portrait"));
        Assert.DoesNotContain("style=", cut.Markup);
        Assert.Contains(".media-tile.is-portrait { width:calc(var(--media-tile-media-height) * 2 / 3); --media-tile-media-aspect:2 / 3; --media-tile-hover-panel-width:clamp(212px,16vw,236px);", css);
        Assert.Contains(".media-tile-hover-panel.is-art-popover.is-portrait { display:flex; flex-direction:column; width:min(calc(100vw - 32px), clamp(212px, 16vw, 236px));", css);
        Assert.Contains("max-height:min(72vh,560px)", css);
        Assert.DoesNotContain("A dreamlike horror comic with mythic scale.", cut.Markup);
        Assert.Empty(cut.FindAll(".media-tile-hover-context-list"));
        Assert.NotEmpty(cut.FindAll("button[aria-label='Details']"));
    }

    [Fact]
    public void MediaTile_BannerPopoverKeepsLandscapeVariant()
    {
        var item = new MediaTileViewModel
        {
            Id = Guid.NewGuid(),
            Title = "Funny AF with Kevin Hart",
            Subtitle = "Netflix",
            MediaKind = "TV",
            AccentColor = "#38BDF8",
            Shape = MediaTileShape.Landscape,
            SurfaceKind = MediaTileSurfaceKind.BannerLandscape,
            HoverLayout = MediaTileHoverLayout.BannerPopover,
            TileImageUrl = "/art/banner.jpg",
            HoverImageUrl = "/art/banner.jpg",
            TileImageFitMode = MediaTileImageFitMode.Fill,
            HoverImageFitMode = MediaTileImageFitMode.Fill,
            LogoUrl = "/art/logo.png",
            NavigationUrl = "/watch/tv",
            PrimaryNavigationUrl = "/watch/tv",
            PrimaryActionLabel = "Continue watching",
        };

        var cut = RenderComponent<MediaTile>(parameters => parameters.Add(component => component.Item, item));

        Assert.NotEmpty(cut.FindAll(".media-tile-hover-panel.is-banner-popover.is-banner-surface"));
        Assert.NotEmpty(cut.FindAll(".media-tile-hover-panel.is-landscape"));
        Assert.DoesNotContain("style=", cut.Markup);
        var css = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Web/Components/MediaTiles/MediaTile.razor.css"));
        Assert.Contains(".media-tile.is-landscape { width:calc(var(--media-tile-media-height) * 16 / 9); --media-tile-media-aspect:16 / 9; --media-tile-hover-panel-width:clamp(330px,25vw,410px);", css);
        Assert.Contains(".media-tile-hover-panel.is-banner-popover.is-landscape { width:var(--media-tile-hover-anchor-width, 100%); max-width:var(--media-tile-hover-anchor-width, 100%); }", css);
        Assert.Contains(".media-tile-hover-panel.is-banner-popover .media-tile-hover-art { width:100%; aspect-ratio:16 / 9; min-height:146px; max-height:none; }", css);
        Assert.Contains(".media-tile-hover-panel.is-banner-popover .media-tile-hover-image { width:100%; height:100%; object-fit:cover; transform:scale(1.035);", css);
        Assert.NotEmpty(cut.FindAll(".media-tile-hover-logo"));
        Assert.NotEmpty(cut.FindAll(".media-tile-hover-image.is-fill"));
    }

    [Fact]
    public void MediaTile_TvSeriesCollectionWithLogoSuppressesDuplicateTileTitleAndShowsRichHover()
    {
        var item = new MediaTileViewModel
        {
            Id = Guid.NewGuid(),
            CollectionId = Guid.NewGuid(),
            Title = "Severance",
            Subtitle = "2 seasons",
            HoverFacts = ["New episodes added", "2 seasons", "Thriller"],
            MediaKind = "TV",
            AccentColor = "#38BDF8",
            Shape = MediaTileShape.Landscape,
            Presentation = MediaTilePresentation.TvSeries,
            SurfaceKind = MediaTileSurfaceKind.BannerLandscape,
            HoverLayout = MediaTileHoverLayout.BannerPopover,
            HoverMode = MediaTileHoverMode.Expanded,
            TileImageUrl = "/art/severance-bg.jpg",
            HoverImageUrl = "/art/severance-bg.jpg",
            LogoUrl = "/art/severance-logo.png",
            PreviewImages = ["/art/severance-poster.jpg", "/art/severance-bg.jpg"],
            NavigationUrl = "/watch/tv/show/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            PrimaryNavigationUrl = "/watch/tv/show/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            PrimaryActionLabel = "Open Show",
            IsCollection = true,
        };

        var cut = RenderComponent<MediaTile>(parameters => parameters.Add(component => component.Item, item));

        Assert.NotEmpty(cut.FindAll(".media-tile-logo"));
        Assert.Empty(cut.FindAll(".media-tile-artwork-stack"));
        Assert.Empty(cut.FindAll(".media-tile-collection-title"));
        Assert.Empty(cut.FindAll(".media-tile-caption"));
        Assert.NotEmpty(cut.FindAll(".media-tile-hover-body"));
        Assert.NotEmpty(cut.FindAll("button[aria-label='Play']"));
        Assert.Empty(cut.FindAll(".media-tile-hover-title"));
        Assert.Contains("New episodes added", cut.Markup);
    }

    [Fact]
    public void MediaTile_BookSeriesCollectionUsesRichPortraitHover()
    {
        var item = new MediaTileViewModel
        {
            Id = Guid.NewGuid(),
            CollectionId = Guid.NewGuid(),
            Title = "The Expanse",
            Subtitle = "9 titles",
            Description = "science fiction book series",
            HoverFacts = ["9 titles", "Science Fiction"],
            Genres = ["Science Fiction", "Space Opera"],
            MediaKind = "Book",
            AccentColor = "#5DCAA5",
            Shape = MediaTileShape.Portrait,
            Presentation = MediaTilePresentation.BookSeries,
            SurfaceKind = MediaTileSurfaceKind.CoverPortrait,
            HoverLayout = MediaTileHoverLayout.ArtOnlyPopover,
            HoverMode = MediaTileHoverMode.Expanded,
            TileImageUrl = "/art/expanse.jpg",
            HoverImageUrl = "/art/expanse.jpg",
            NavigationUrl = "/read/books?grouping=series",
            PrimaryNavigationUrl = "/read/books?grouping=series",
            PrimaryActionLabel = "Open Series",
            IsCollection = true,
        };

        var cut = RenderComponent<MediaTile>(parameters => parameters.Add(component => component.Item, item));

        Assert.NotEmpty(cut.FindAll(".media-tile-hover-panel.is-art-popover.is-portrait.is-book-series.is-collection-hover"));
        Assert.NotEmpty(cut.FindAll(".media-tile-hover-body"));
        Assert.NotEmpty(cut.FindAll("button[aria-label='Open Series']"));
        Assert.NotEmpty(cut.FindAll(".media-tile-hover-title"));
        Assert.Contains("science fiction book series", cut.Markup);
    }

    [Fact]
    public void MediaTile_OrderedSeriesCollectionUsesSeriesStripAndOverflowLabel()
    {
        var item = new MediaTileViewModel
        {
            Id = Guid.NewGuid(),
            CollectionId = Guid.NewGuid(),
            Title = "Foundation Series",
            Subtitle = "6 titles",
            MediaKind = "Book",
            AccentColor = "#5DCAA5",
            Shape = MediaTileShape.Landscape,
            Presentation = MediaTilePresentation.BookSeries,
            SurfaceKind = MediaTileSurfaceKind.BannerLandscape,
            HoverLayout = MediaTileHoverLayout.BannerPopover,
            HoverMode = MediaTileHoverMode.Expanded,
            TileImageUrl = "/art/foundation-bg.jpg",
            HoverImageUrl = "/art/foundation-bg.jpg",
            NavigationUrl = "/details/bookseries/foundation",
            PrimaryNavigationUrl = "/details/bookseries/foundation",
            PrimaryActionLabel = "Open Series",
            IsCollection = true,
            PreviewTotalCount = 6,
            ArtworkStackItems =
            [
                new ArtworkStackItem { Id = "1", Title = "Foundation", ImageUrl = "/covers/1.jpg", MediaType = "Book", Shape = ArtworkShape.Portrait, Position = "1" },
                new ArtworkStackItem { Id = "2", Title = "Foundation and Empire", ImageUrl = "/covers/2.jpg", MediaType = "Book", Shape = ArtworkShape.Portrait, Position = "2" },
                new ArtworkStackItem { Id = "3", Title = "Second Foundation", ImageUrl = "/covers/3.jpg", MediaType = "Book", Shape = ArtworkShape.Portrait, Position = "3" },
                new ArtworkStackItem { Id = "4", Title = "Foundation's Edge", ImageUrl = "/covers/4.jpg", MediaType = "Book", Shape = ArtworkShape.Portrait, Position = "4" },
            ],
        };

        var cut = RenderComponent<MediaTile>(parameters => parameters.Add(component => component.Item, item));

        Assert.NotEmpty(cut.FindAll(".media-tile.is-ordered-series-card"));
        Assert.NotEmpty(cut.FindAll(".artwork-stack--seriesstrip"));
        Assert.NotEmpty(cut.FindAll(".media-tile-hover-panel.is-ordered-series-hover"));
        Assert.NotEmpty(cut.FindAll(".media-tile-hover-series-stack"));
        Assert.Equal(4, cut.FindAll(".media-tile-artwork-stack--series-tile .artwork-stack__position").Count);
        Assert.Equal(4, cut.FindAll(".media-tile-hover-series-stack .artwork-stack__position").Count);
        Assert.NotEmpty(cut.FindAll(".media-tile-collection-kind-icon"));
        Assert.Contains("+2 more", cut.Markup);
    }

    [Fact]
    public void MediaTile_ComicHoverDoesNotRepeatSubtitleFactsInMeta()
    {
        var item = new MediaTileViewModel
        {
            Id = Guid.NewGuid(),
            WorkId = Guid.NewGuid(),
            Title = "Saga Chapter One",
            Subtitle = "Saga - Issue #1",
            MediaKind = "Comic",
            Shape = MediaTileShape.Portrait,
            SurfaceKind = MediaTileSurfaceKind.CoverPortrait,
            HoverLayout = MediaTileHoverLayout.ArtOnlyPopover,
            HoverMode = MediaTileHoverMode.Expanded,
            TileImageUrl = "/art/saga-1.jpg",
            HoverImageUrl = "/art/saga-1.jpg",
            HoverFacts = ["Saga", "Issue #1", "Science Fiction"],
            MetaText = "Saga / Issue #1 / Science Fiction",
            NavigationUrl = "/book/saga-1?mode=read",
            PrimaryNavigationUrl = "/book/saga-1?mode=read",
            PrimaryActionLabel = "Read",
        };

        var cut = RenderComponent<MediaTile>(parameters => parameters.Add(component => component.Item, item));

        Assert.Contains("Saga - Issue #1", cut.Markup);
        var hoverFact = Assert.Single(cut.FindAll(".media-tile-hover-fact"));
        Assert.Contains("Science Fiction", hoverFact.TextContent);
        Assert.Empty(cut.FindAll(".media-tile-hover-meta-row"));
    }

    [Fact]
    public void MediaTile_HoverDescriptionOnlyRendersCompactOneLineDescriptors()
    {
        var item = new MediaTileViewModel
        {
            Id = Guid.NewGuid(),
            Title = "Breaking Bad",
            Description = "Walter White, a New Mexico chemistry teacher, is diagnosed with Stage III cancer and given a prognosis of only two years left to live. He becomes filled with a sense of fearlessness.",
            MediaKind = "TV",
            Shape = MediaTileShape.Landscape,
            SurfaceKind = MediaTileSurfaceKind.BannerLandscape,
            HoverLayout = MediaTileHoverLayout.BannerPopover,
            HoverMode = MediaTileHoverMode.Expanded,
            TileImageUrl = "/art/breaking-bad.jpg",
            HoverImageUrl = "/art/breaking-bad.jpg",
            NavigationUrl = "/watch/tv/show/breaking-bad",
            PrimaryNavigationUrl = "/watch/tv/show/breaking-bad",
            PrimaryActionLabel = "Open Show",
        };

        var cut = RenderComponent<MediaTile>(parameters => parameters.Add(component => component.Item, item));

        Assert.Empty(cut.FindAll(".media-tile-hover-description"));
        Assert.DoesNotContain("Walter White", cut.Markup);
    }

    [Fact]
    public void MediaTile_MediaClickOpensDetailsAndPrimaryButtonKeepsPrimaryRoute()
    {
        var item = new MediaTileViewModel
        {
            Id = Guid.NewGuid(),
            Title = "Project Hail Mary",
            Subtitle = "Andy Weir",
            MediaKind = "Book",
            AccentColor = "#5DCAA5",
            Shape = MediaTileShape.Portrait,
            SurfaceKind = MediaTileSurfaceKind.CoverPortrait,
            HoverLayout = MediaTileHoverLayout.ArtOnlyPopover,
            TileImageUrl = "/art/hail-mary.jpg",
            HoverImageUrl = "/art/hail-mary.jpg",
            NavigationUrl = "/read/books",
            DetailsNavigationUrl = "/read/books/work/project-hail-mary",
            PrimaryNavigationUrl = "/reader/project-hail-mary",
            PrimaryActionLabel = "Read",
        };
        var navigation = Services.GetRequiredService<NavigationManager>();
        var cut = RenderComponent<MediaTile>(parameters => parameters.Add(component => component.Item, item));

        cut.Find(".media-tile-media").Click();
        Assert.EndsWith("/read/books/work/project-hail-mary", navigation.Uri, StringComparison.Ordinal);

        cut.Find(".media-tile-hover-art").Click();
        Assert.EndsWith("/read/books/work/project-hail-mary", navigation.Uri, StringComparison.Ordinal);

        cut.Find(".media-tile-hover-panel").Click();
        Assert.EndsWith("/read/books/work/project-hail-mary", navigation.Uri, StringComparison.Ordinal);

        cut.Find("button[aria-label='Read']").Click();
        Assert.EndsWith("/reader/project-hail-mary", navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void MediaTile_KeyboardActivationOpensDetails()
    {
        var item = new MediaTileViewModel
        {
            Id = Guid.NewGuid(),
            Title = "Dune",
            Subtitle = "Frank Herbert",
            MediaKind = "Book",
            AccentColor = "#5DCAA5",
            Shape = MediaTileShape.Portrait,
            SurfaceKind = MediaTileSurfaceKind.CoverPortrait,
            HoverLayout = MediaTileHoverLayout.ArtOnlyPopover,
            TileImageUrl = "/art/dune.jpg",
            HoverImageUrl = "/art/dune.jpg",
            NavigationUrl = "/read/books",
            DetailsNavigationUrl = "/read/books/work/dune",
            PrimaryNavigationUrl = "/reader/dune",
            PrimaryActionLabel = "Read",
        };
        var navigation = Services.GetRequiredService<NavigationManager>();
        var cut = RenderComponent<MediaTile>(parameters => parameters.Add(component => component.Item, item));

        cut.Find(".media-tile").TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "Enter" });

        Assert.EndsWith("/read/books/work/dune", navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void MediaTile_CanHideLaneRedundantBadges()
    {
        var item = new MediaTileViewModel
        {
            Id = Guid.NewGuid(),
            Title = "Blade Runner 2049",
            Subtitle = "Warner Bros.",
            MediaKind = "Movie",
            MetaText = "Movie / 1994",
            SourceBadgeLabel = "Warner Bros.",
            Shape = MediaTileShape.Landscape,
            SurfaceKind = MediaTileSurfaceKind.BannerLandscape,
            HoverLayout = MediaTileHoverLayout.BannerPopover,
            HoverMode = MediaTileHoverMode.Expanded,
            TileImageUrl = "/art/blade-runner.jpg",
            HoverImageUrl = "/art/blade-runner.jpg",
            NavigationUrl = "/watch/movies",
        };

        var cut = RenderComponent<MediaTile>(parameters => parameters
            .Add(component => component.Item, item)
            .Add(component => component.HideMediaKindBadge, true)
            .Add(component => component.HideSourceBadge, true));

        Assert.Empty(cut.FindAll(".media-tile-badge"));
        Assert.Empty(cut.FindAll(".media-tile-source-badge"));
        Assert.Empty(cut.FindAll(".media-tile-meta-icon"));
        Assert.Contains("1994", cut.Markup);
    }

    [Fact]
    public void MediaTile_DisabledHoverDoesNotRenderHoverPanel()
    {
        var item = new MediaTileViewModel
        {
            Id = Guid.NewGuid(),
            Title = "Quiet Tile",
            MediaKind = "Book",
            Shape = MediaTileShape.Portrait,
            SurfaceKind = MediaTileSurfaceKind.CoverPortrait,
            TileImageUrl = "/art/quiet.jpg",
            HoverImageUrl = "/art/quiet-large.jpg",
            NavigationUrl = "/read/books",
        };

        var cut = RenderComponent<MediaTile>(parameters => parameters
            .Add(component => component.Item, item)
            .Add(component => component.HoverMode, MediaTileHoverMode.None));

        Assert.Empty(cut.FindAll(".media-tile-hover-panel"));
        Assert.Empty(cut.FindAll(".media-tile-touch-toggle"));
    }

    [Fact]
    public void MediaTile_PreviewHoverRendersArtWithoutExpandedBody()
    {
        var item = new MediaTileViewModel
        {
            Id = Guid.NewGuid(),
            Title = "Preview Tile",
            MediaKind = "Book",
            Shape = MediaTileShape.Portrait,
            SurfaceKind = MediaTileSurfaceKind.CoverPortrait,
            HoverLayout = MediaTileHoverLayout.ArtOnlyPopover,
            TileImageUrl = "/art/preview-s.jpg",
            HoverImageUrl = "/art/preview-m.jpg",
            NavigationUrl = "/read/books",
        };

        var cut = RenderComponent<MediaTile>(parameters => parameters
            .Add(component => component.Item, item)
            .Add(component => component.HoverMode, MediaTileHoverMode.Preview));

        Assert.NotEmpty(cut.FindAll(".media-tile-hover-panel.is-hover-preview"));
        Assert.NotEmpty(cut.FindAll(".media-tile-hover-image"));
        Assert.Empty(cut.FindAll(".media-tile-hover-body"));
    }

    [Fact]
    public void DiscoveryHero_CoverSurfaceUsesCoverLayoutAndVibes()
    {
        var hero = new DiscoveryHeroViewModel
        {
            Title = "Project Hail Mary",
            Subtitle = "Andy Weir",
            Tldr = "A lone astronaut tries to save Earth.",
            VibeTags = ["Hopeful", "Tense"],
            AccentColor = "#5DCAA5",
            HeroBackgroundImageUrl = "/art/book-cover.jpg",
            PreviewImageUrl = "/art/book-cover.jpg",
            SurfaceKind = MediaTileSurfaceKind.CoverPortrait,
            PrimaryActionLabel = "Continue reading",
            PrimaryNavigationUrl = "/read/books",
        };

        var cut = RenderComponent<DiscoveryHero>(parameters => parameters.Add(component => component.Hero, hero));

        Assert.NotEmpty(cut.FindAll(".discovery-hero-shell.is-cover-hero"));
        Assert.NotEmpty(cut.FindAll(".discovery-hero-preview.is-portrait-preview"));
        Assert.Equal(2, cut.FindAll(".discovery-hero-chip").Count);
        Assert.Contains("A lone astronaut tries to save Earth.", cut.Markup);
    }

    [Fact]
    public void DiscoveryHero_BannerSurfaceUsesBannerLayout()
    {
        var hero = new DiscoveryHeroViewModel
        {
            Title = "Funny AF with Kevin Hart",
            Subtitle = "Netflix",
            AccentColor = "#38BDF8",
            HeroBackgroundImageUrl = "/art/show-banner.jpg",
            PreviewImageUrl = "/art/show-cover.jpg",
            LogoUrl = "/art/show-logo.png",
            SurfaceKind = MediaTileSurfaceKind.BannerLandscape,
            PrimaryActionLabel = "Continue watching",
            PrimaryNavigationUrl = "/watch/tv",
        };

        var cut = RenderComponent<DiscoveryHero>(parameters => parameters.Add(component => component.Hero, hero));

        Assert.NotEmpty(cut.FindAll(".discovery-hero-shell.is-banner-hero"));
        Assert.NotEmpty(cut.FindAll(".discovery-hero-logo"));
        Assert.NotEmpty(cut.FindAll(".discovery-hero-preview.is-portrait-preview"));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MediaEngine.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
