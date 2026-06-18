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

        Assert.NotEmpty(cut.FindAll(".media-tile-hover-panel.is-art-popover.is-cover-square"));
        Assert.DoesNotContain("style=", cut.Markup);
        Assert.Contains(".media-tile.is-square { width:var(--media-tile-media-height); --media-tile-media-aspect:1 / 1; --media-tile-hover-panel-width:clamp(248px,18vw,292px);", css);
        Assert.Contains("flex:0 0 auto", css);
        Assert.Contains("aspect-ratio:var(--media-tile-media-aspect)", css);
        Assert.Contains(".media-tile-image.is-contained { object-fit:contain; padding:0; background:transparent; }", css);
        Assert.Contains(".media-tile-hover-panel.is-visible { opacity:1; visibility:visible; pointer-events:none;", css);
        Assert.Contains(".media-tile-hover-actions, .media-tile-icon-button { pointer-events:auto; }", css);
        Assert.DoesNotContain("overflow:hidden auto", css);
        Assert.NotEmpty(cut.FindAll(".media-tile-hover-image.is-contained"));
        Assert.Equal(2, cut.FindAll(".media-tile-chip").Count);
        Assert.DoesNotContain("A sharp indie record with close harmonies.", cut.Markup);
        Assert.Empty(cut.FindAll(".media-tile-hover-logo"));
        Assert.Empty(cut.FindAll("button[aria-label='Details']"));
    }

    [Fact]
    public void MediaTile_PortraitPopoverUsesSideBySidePreview()
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
        Assert.Contains(".media-tile.is-portrait { width:calc(var(--media-tile-media-height) * 2 / 3); --media-tile-media-aspect:2 / 3; --media-tile-hover-panel-width:clamp(360px,31vw,440px);", css);
        Assert.Contains("grid-template-columns:minmax(128px,34%) minmax(184px,1fr)", css);
        Assert.Contains("max-height:min(62vh,420px)", css);
        Assert.DoesNotContain("A dreamlike horror comic with mythic scale.", cut.Markup);
        Assert.Empty(cut.FindAll(".media-tile-hover-context-list"));
        Assert.Empty(cut.FindAll("button[aria-label='Details']"));
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
        Assert.Contains(".media-tile.is-landscape { width:calc(var(--media-tile-media-height) * 16 / 9); --media-tile-media-aspect:16 / 9; --media-tile-hover-panel-width:clamp(330px,25vw,410px);", File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Web/Components/MediaTiles/MediaTile.razor.css")));
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
        Assert.NotEmpty(cut.FindAll("button[aria-label='Open Show']"));
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
            Description = "A long-running space opera series.",
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
        Assert.Contains("A long-running space opera series.", cut.Markup);
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
