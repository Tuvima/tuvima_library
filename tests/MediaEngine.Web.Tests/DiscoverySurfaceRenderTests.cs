using Bunit;
using MediaEngine.Web.Components.Discovery;
using MediaEngine.Web.Models.ViewDTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace MediaEngine.Web.Tests;

public sealed class DiscoverySurfaceRenderTests : TestContext
{
    public DiscoverySurfaceRenderTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }

    [Fact]
    public void DiscoveryCard_ArtOnlyPopoverUsesCompactArtPreview()
    {
        var item = new DiscoveryCardViewModel
        {
            Id = Guid.NewGuid(),
            Title = "The Record",
            Subtitle = "boygenius",
            Tldr = "A sharp indie record with close harmonies.",
            VibeTags = ["Wry", "Kinetic"],
            MediaKind = "Music",
            AccentColor = "#1ED760",
            Shape = DiscoveryCardShape.Square,
            SurfaceKind = DiscoverySurfaceKind.CoverSquare,
            HoverLayout = DiscoveryHoverLayout.ArtOnlyPopover,
            TileImageUrl = "/art/album.jpg",
            HoverImageUrl = "/art/album.jpg",
            NavigationUrl = "/listen/music",
            PrimaryNavigationUrl = "/listen/music",
            PrimaryActionLabel = "Open",
        };

        var cut = RenderComponent<DiscoveryCard>(parameters => parameters.Add(component => component.Item, item));

        Assert.NotEmpty(cut.FindAll(".discovery-card-hover-panel.is-art-popover.is-cover-square"));
        Assert.Contains("--discovery-hover-panel-width: clamp(248px, 18vw, 292px)", cut.Markup);
        Assert.Contains("flex:0 0 auto", cut.Markup);
        Assert.Contains("aspect-ratio:var(--discovery-card-media-aspect)", cut.Markup);
        Assert.Contains(".discovery-card-image.is-contained { object-fit:contain; padding:0; background:transparent; }", cut.Markup);
        Assert.DoesNotContain("overflow:hidden auto", cut.Markup);
        Assert.NotEmpty(cut.FindAll(".discovery-card-hover-image.is-contained"));
        Assert.Equal(2, cut.FindAll(".discovery-card-chip").Count);
        Assert.DoesNotContain("A sharp indie record with close harmonies.", cut.Markup);
        Assert.Empty(cut.FindAll(".discovery-card-hover-logo"));
        Assert.Empty(cut.FindAll("button[aria-label='Details']"));
    }

    [Fact]
    public void DiscoveryCard_PortraitPopoverUsesSideBySidePreview()
    {
        var item = new DiscoveryCardViewModel
        {
            Id = Guid.NewGuid(),
            Title = "The Sandman",
            Subtitle = "Neil Gaiman",
            Tldr = "A dreamlike horror comic with mythic scale.",
            Genres = ["Fantasy", "Horror"],
            MediaKind = "Comic",
            AccentColor = "#C9922E",
            Shape = DiscoveryCardShape.Portrait,
            SurfaceKind = DiscoverySurfaceKind.CoverPortrait,
            HoverLayout = DiscoveryHoverLayout.ArtOnlyPopover,
            TileImageUrl = "/art/sandman.jpg",
            HoverImageUrl = "/art/sandman.jpg",
            NavigationUrl = "/read/comics",
            DetailsNavigationUrl = "/read/comics/work/123",
            PrimaryNavigationUrl = "/reader/123",
            PrimaryActionLabel = "Read",
        };

        var cut = RenderComponent<DiscoveryCard>(parameters => parameters.Add(component => component.Item, item));

        Assert.NotEmpty(cut.FindAll(".discovery-card-hover-panel.is-art-popover.is-portrait.is-cover-portrait"));
        Assert.Contains("--discovery-hover-panel-width: clamp(430px, 40vw, 560px)", cut.Markup);
        Assert.Contains("grid-template-columns:minmax(150px,38%) minmax(220px,1fr)", cut.Markup);
        Assert.Contains("max-height:min(62vh,420px)", cut.Markup);
        Assert.DoesNotContain("A dreamlike horror comic with mythic scale.", cut.Markup);
        Assert.Empty(cut.FindAll(".discovery-card-hover-context-list"));
        Assert.Empty(cut.FindAll("button[aria-label='Details']"));
    }

    [Fact]
    public void DiscoveryCard_BannerPopoverKeepsLandscapeVariant()
    {
        var item = new DiscoveryCardViewModel
        {
            Id = Guid.NewGuid(),
            Title = "Funny AF with Kevin Hart",
            Subtitle = "Netflix",
            MediaKind = "TV",
            AccentColor = "#38BDF8",
            Shape = DiscoveryCardShape.Landscape,
            SurfaceKind = DiscoverySurfaceKind.BannerLandscape,
            HoverLayout = DiscoveryHoverLayout.BannerPopover,
            TileImageUrl = "/art/banner.jpg",
            HoverImageUrl = "/art/banner.jpg",
            TileImageFitMode = DiscoveryImageFitMode.Fill,
            HoverImageFitMode = DiscoveryImageFitMode.Fill,
            LogoUrl = "/art/logo.png",
            NavigationUrl = "/watch/tv",
            PrimaryNavigationUrl = "/watch/tv",
            PrimaryActionLabel = "Continue watching",
        };

        var cut = RenderComponent<DiscoveryCard>(parameters => parameters.Add(component => component.Item, item));

        Assert.NotEmpty(cut.FindAll(".discovery-card-hover-panel.is-banner-popover.is-banner-surface"));
        Assert.NotEmpty(cut.FindAll(".discovery-card-hover-panel.is-landscape"));
        Assert.Contains("--discovery-hover-panel-width: clamp(360px, 28vw, 440px)", cut.Markup);
        Assert.NotEmpty(cut.FindAll(".discovery-card-hover-logo"));
        Assert.NotEmpty(cut.FindAll(".discovery-card-hover-image.is-fill"));
    }

    [Fact]
    public void DiscoveryCard_MediaClickOpensDetailsAndPrimaryButtonKeepsPrimaryRoute()
    {
        var item = new DiscoveryCardViewModel
        {
            Id = Guid.NewGuid(),
            Title = "Project Hail Mary",
            Subtitle = "Andy Weir",
            MediaKind = "Book",
            AccentColor = "#5DCAA5",
            Shape = DiscoveryCardShape.Portrait,
            SurfaceKind = DiscoverySurfaceKind.CoverPortrait,
            HoverLayout = DiscoveryHoverLayout.ArtOnlyPopover,
            TileImageUrl = "/art/hail-mary.jpg",
            HoverImageUrl = "/art/hail-mary.jpg",
            NavigationUrl = "/read/books",
            DetailsNavigationUrl = "/read/books/work/project-hail-mary",
            PrimaryNavigationUrl = "/reader/project-hail-mary",
            PrimaryActionLabel = "Read",
        };
        var navigation = Services.GetRequiredService<NavigationManager>();
        var cut = RenderComponent<DiscoveryCard>(parameters => parameters.Add(component => component.Item, item));

        cut.Find(".discovery-card-media").Click();
        Assert.EndsWith("/read/books/work/project-hail-mary", navigation.Uri, StringComparison.Ordinal);

        cut.Find(".discovery-card-hover-art").Click();
        Assert.EndsWith("/read/books/work/project-hail-mary", navigation.Uri, StringComparison.Ordinal);

        cut.Find("button[aria-label='Read']").Click();
        Assert.EndsWith("/reader/project-hail-mary", navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoveryCard_KeyboardActivationOpensDetails()
    {
        var item = new DiscoveryCardViewModel
        {
            Id = Guid.NewGuid(),
            Title = "Dune",
            Subtitle = "Frank Herbert",
            MediaKind = "Book",
            AccentColor = "#5DCAA5",
            Shape = DiscoveryCardShape.Portrait,
            SurfaceKind = DiscoverySurfaceKind.CoverPortrait,
            HoverLayout = DiscoveryHoverLayout.ArtOnlyPopover,
            TileImageUrl = "/art/dune.jpg",
            HoverImageUrl = "/art/dune.jpg",
            NavigationUrl = "/read/books",
            DetailsNavigationUrl = "/read/books/work/dune",
            PrimaryNavigationUrl = "/reader/dune",
            PrimaryActionLabel = "Read",
        };
        var navigation = Services.GetRequiredService<NavigationManager>();
        var cut = RenderComponent<DiscoveryCard>(parameters => parameters.Add(component => component.Item, item));

        cut.Find(".discovery-card").TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "Enter" });

        Assert.EndsWith("/read/books/work/dune", navigation.Uri, StringComparison.Ordinal);
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
            SurfaceKind = DiscoverySurfaceKind.CoverPortrait,
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
            SurfaceKind = DiscoverySurfaceKind.BannerLandscape,
            PrimaryActionLabel = "Continue watching",
            PrimaryNavigationUrl = "/watch/tv",
        };

        var cut = RenderComponent<DiscoveryHero>(parameters => parameters.Add(component => component.Hero, hero));

        Assert.NotEmpty(cut.FindAll(".discovery-hero-shell.is-banner-hero"));
        Assert.NotEmpty(cut.FindAll(".discovery-hero-logo"));
        Assert.NotEmpty(cut.FindAll(".discovery-hero-preview.is-portrait-preview"));
    }
}
