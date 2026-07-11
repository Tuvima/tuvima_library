using Bunit;
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
    public void MediaTile_RestingCardIsArtworkOnlyAndHoverIsCompactIdentity()
    {
        var item = new MediaTileViewModel
        {
            Id = Guid.NewGuid(),
            WorkId = Guid.NewGuid(),
            Title = "Leviathan Wakes",
            Subtitle = "The Expanse, Book 1",
            Description = "This belongs on the detail page only.",
            HoverFacts = ["James S. A. Corey", "M", "2011", "592 pages", "★ 4.3"],
            MediaKind = "Book",
            Shape = MediaTileShape.Portrait,
            SurfaceKind = MediaTileSurfaceKind.CoverPortrait,
            HoverLayout = MediaTileHoverLayout.ArtOnlyPopover,
            HoverMode = MediaTileHoverMode.Expanded,
            TileImageUrl = "/art/leviathan-wakes.jpg",
            HoverImageUrl = "/art/leviathan-wakes.jpg",
            NavigationUrl = "/book/1",
            DetailsNavigationUrl = "/book/1",
            PrimaryNavigationUrl = "/read/1",
            PrimaryActionLabel = "Read",
            ProgressPct = 42,
        };

        var cut = RenderComponent<MediaTile>(parameters => parameters.Add(component => component.Item, item));

        Assert.Equal("Leviathan Wakes", cut.Find(".media-tile").GetAttribute("aria-label"));
        Assert.NotEmpty(cut.FindAll(".media-tile-image"));
        Assert.Empty(cut.FindAll(".media-tile-caption"));
        Assert.Empty(cut.FindAll(".media-tile-badge"));
        Assert.Empty(cut.FindAll(".media-tile-quality-badge"));
        Assert.Empty(cut.FindAll(".media-tile-source-badge"));
        Assert.Empty(cut.FindAll(".media-tile-progress-strip"));
        Assert.Empty(cut.FindAll(".media-tile-logo"));
        Assert.NotEmpty(cut.FindAll("div[style*='display: contents']"));
        Assert.NotEmpty(cut.FindAll(".media-tile-hover-cover-stage.is-book-stage"));
        Assert.NotEmpty(cut.FindAll(".media-tile-book-pages"));
        Assert.NotEmpty(cut.FindAll(".media-tile-book-spine"));
        Assert.Contains("Leviathan Wakes", cut.Find(".media-tile-hover-title").TextContent);
        Assert.Contains("The Expanse, Book 1", cut.Find(".media-tile-hover-subtitle").TextContent);
        Assert.Contains("592 pages", cut.Markup);
        Assert.Contains("4.3", cut.Find(".media-tile-rating-pill").TextContent);
        Assert.DoesNotContain(item.Description, cut.Markup);
        Assert.NotEmpty(cut.FindAll("button[aria-label='Dislike']"));
        Assert.NotEmpty(cut.FindAll("button[aria-label='Like']"));
        Assert.NotEmpty(cut.FindAll("button[aria-label='Love']"));
        Assert.NotEmpty(cut.FindAll(".media-tile-hover-progress"));

        cut.Find(".media-tile").TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "ArrowDown" });
        Assert.Contains("is-expanded", cut.Find(".media-tile").ClassList);
    }

    [Fact]
    public void MediaTile_EnrichedLogoReplacesHoverTitleButNotRestingArtwork()
    {
        var item = new MediaTileViewModel
        {
            Id = Guid.NewGuid(),
            WorkId = Guid.NewGuid(),
            Title = "Arrival",
            HoverFacts = ["PG-13", "2016", "1h 56m", "★ 7.9"],
            MediaKind = "Movie",
            Shape = MediaTileShape.Portrait,
            SurfaceKind = MediaTileSurfaceKind.CoverPortrait,
            HoverLayout = MediaTileHoverLayout.BannerPopover,
            HoverMode = MediaTileHoverMode.Expanded,
            TileImageUrl = "/art/arrival-poster.jpg",
            HoverImageUrl = "/art/arrival-background.jpg",
            LogoUrl = "/art/arrival-logo.png",
            NavigationUrl = "/watch/movie/1",
            PrimaryNavigationUrl = "/watch/player/1",
            PrimaryActionLabel = "Play",
        };

        var cut = RenderComponent<MediaTile>(parameters => parameters.Add(component => component.Item, item));

        Assert.Empty(cut.FindAll(".media-tile-logo"));
        Assert.NotEmpty(cut.FindAll(".media-tile-hover-logo"));
        Assert.Empty(cut.FindAll(".media-tile-hover-title"));
        Assert.Contains("PG-13", cut.Markup);
        Assert.Contains("1h 56m", cut.Markup);
    }

    [Fact]
    public void MediaTile_LandscapeSeriesKeepsOrderedArtworkWithoutRestingCopy()
    {
        var item = new MediaTileViewModel
        {
            Id = Guid.NewGuid(),
            CollectionId = Guid.NewGuid(),
            Title = "Foundation Series",
            MediaKind = "Book",
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
            ArtworkStackItems =
            [
                new ArtworkStackItem { Id = "1", Title = "Foundation", ImageUrl = "/covers/1.jpg", MediaType = "Book", Shape = ArtworkShape.Portrait, Position = "1" },
                new ArtworkStackItem { Id = "2", Title = "Foundation and Empire", ImageUrl = "/covers/2.jpg", MediaType = "Book", Shape = ArtworkShape.Portrait, Position = "2" },
            ],
        };

        var cut = RenderComponent<MediaTile>(parameters => parameters.Add(component => component.Item, item));

        Assert.NotEmpty(cut.FindAll(".media-tile.is-landscape.is-ordered-series-card"));
        Assert.NotEmpty(cut.FindAll(".artwork-stack--seriesstrip"));
        Assert.Empty(cut.FindAll(".media-tile-collection-copy"));
        Assert.Empty(cut.FindAll(".media-tile-caption"));
        Assert.NotEmpty(cut.FindAll(".media-tile-hover-series-stack"));
    }

    [Fact]
    public void MediaTile_PrimaryCallbackAndDetailsNavigationRemainSeparate()
    {
        var details = "/book/1";
        var item = new MediaTileViewModel
        {
            Id = Guid.NewGuid(),
            WorkId = Guid.NewGuid(),
            Title = "Project Hail Mary",
            MediaKind = "Book",
            Shape = MediaTileShape.Portrait,
            SurfaceKind = MediaTileSurfaceKind.CoverPortrait,
            HoverLayout = MediaTileHoverLayout.ArtOnlyPopover,
            TileImageUrl = "/art/hail-mary.jpg",
            HoverImageUrl = "/art/hail-mary.jpg",
            NavigationUrl = details,
            DetailsNavigationUrl = details,
            PrimaryNavigationUrl = "/read/1",
            PrimaryActionLabel = "Read",
        };
        MediaTileViewModel? selected = null;
        var cut = RenderComponent<MediaTile>(parameters => parameters
            .Add(component => component.Item, item)
            .Add(component => component.OnPrimaryClicked, EventCallback.Factory.Create<MediaTileViewModel>(this, value => selected = value)));

        cut.Find("button[aria-label='Read']").Click();
        Assert.Same(item, selected);

        cut.Find(".media-tile-media").Click();
        var nav = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith(details, nav.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void MediaTile_CssAndJavascriptProvideWideFallbackAndSiblingShift()
    {
        var css = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Web/Components/MediaTiles/MediaTile.razor.css"));
        var appJs = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Web/wwwroot/app.js"));

        Assert.Contains("grid-template-columns: minmax(520px, 1.25fr) minmax(360px, .75fr)", css);
        Assert.Contains("--media-tile-media-height: clamp(400px, 25vw, 500px)", css);
        Assert.Contains(".media-tile-book-pages", css);
        Assert.Contains("background: transparent", css);
        Assert.Contains(".media-tile-feedback-control:focus-within", css);
        Assert.Contains("width: 188px", css);
        Assert.Contains("--media-tile-hover-anchor-height", css);
        Assert.Contains("clamp(820px, 52vw, 980px)", css);
        Assert.Contains("object-fit: contain", css);
        Assert.Contains("var(--art-bg-base-dark, #080c12)", css);
        Assert.Contains("window.shiftMediaTileHoverRow", appJs);
        Assert.Contains("--media-tile-row-shift-x", appJs);
        Assert.Contains("cardEl.closest('.media-tile-shelf-scroll, .media-tile-grid')", appJs);
        Assert.Contains("var panelTop = cardRect.top;", appJs);
        Assert.Contains("window.clearMediaTileHoverRowShift", appJs);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MediaEngine.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
