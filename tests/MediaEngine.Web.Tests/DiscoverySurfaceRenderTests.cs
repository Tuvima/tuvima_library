using Bunit;
using MediaEngine.Web.Components.Discovery;
using MediaEngine.Web.Models.ViewDTOs;
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
    public void DiscoveryCard_ArtOnlyPopoverUsesFullArtAndAiCopy()
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
        Assert.Contains("--discovery-hover-panel-width: clamp(260px, 19vw, 312px)", cut.Markup);
        Assert.DoesNotContain("overflow:hidden auto", cut.Markup);
        Assert.NotEmpty(cut.FindAll(".discovery-card-hover-image.is-contained"));
        Assert.Equal(2, cut.FindAll(".discovery-card-chip").Count);
        Assert.Contains("A sharp indie record with close harmonies.", cut.Markup);
        Assert.Empty(cut.FindAll(".discovery-card-hover-logo"));
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
        Assert.Contains("--discovery-hover-panel-width: clamp(348px, 27vw, 430px)", cut.Markup);
        Assert.NotEmpty(cut.FindAll(".discovery-card-hover-logo"));
        Assert.NotEmpty(cut.FindAll(".discovery-card-hover-image.is-fill"));
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
