using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Discovery;

namespace MediaEngine.Web.Tests;

public sealed class DiscoveryArtworkResolverTests
{
    [Fact]
    public void Resolve_LandscapeDimensionsProduceLandscapeTileAndHover()
    {
        var surface = DiscoveryArtworkResolver.Resolve(
            DiscoveryBucket.Movie,
            DiscoveryCardPresentation.Default,
            [new ArtworkVariant(ArtworkRole.Background, "/art/backdrop.jpg", 1920, 1080)]);

        Assert.Equal(DiscoveryCardShape.Landscape, surface.Shape);
        Assert.Equal(DiscoverySurfaceKind.BannerLandscape, surface.SurfaceKind);
        Assert.Equal(DiscoveryHoverLayout.BannerPopover, surface.HoverLayout);
        Assert.Equal("/art/backdrop.jpg", surface.TileImageUrl);
    }

    [Fact]
    public void Resolve_SquareDimensionsProduceSquareTileAndHover()
    {
        var surface = DiscoveryArtworkResolver.Resolve(
            DiscoveryBucket.Music,
            DiscoveryCardPresentation.Album,
            [new ArtworkVariant(ArtworkRole.Cover, "/art/album.jpg", 1000, 1000)]);

        Assert.Equal(DiscoveryCardShape.Square, surface.Shape);
        Assert.Equal(DiscoverySurfaceKind.CoverSquare, surface.SurfaceKind);
        Assert.Equal(DiscoveryHoverLayout.ArtOnlyPopover, surface.HoverLayout);
    }

    [Fact]
    public void Resolve_PortraitCoverDimensionsProducePortraitTileAndHover()
    {
        var surface = DiscoveryArtworkResolver.Resolve(
            DiscoveryBucket.Book,
            DiscoveryCardPresentation.Default,
            [new ArtworkVariant(ArtworkRole.Cover, "/art/book.jpg", 600, 900)]);

        Assert.Equal(DiscoveryCardShape.Portrait, surface.Shape);
        Assert.Equal(DiscoverySurfaceKind.CoverPortrait, surface.SurfaceKind);
        Assert.Equal(DiscoveryHoverLayout.ArtOnlyPopover, surface.HoverLayout);
    }

    [Fact]
    public void Resolve_MovieCoverOnlyDoesNotBecomeLandscape()
    {
        var surface = DiscoveryArtworkResolver.Resolve(
            DiscoveryBucket.Movie,
            DiscoveryCardPresentation.Default,
            [new ArtworkVariant(ArtworkRole.Cover, "/art/poster.jpg", 600, 900)]);

        Assert.Equal(DiscoveryCardShape.Portrait, surface.Shape);
        Assert.Equal(DiscoverySurfaceKind.CoverPortrait, surface.SurfaceKind);
        Assert.Equal("/art/poster.jpg", surface.TileImageUrl);
    }

    [Fact]
    public void Resolve_ArtistDoesNotFallBackToAlbumCover()
    {
        var surface = DiscoveryArtworkResolver.Resolve(
            DiscoveryBucket.Music,
            DiscoveryCardPresentation.Artist,
            [new ArtworkVariant(ArtworkRole.Cover, "/art/album.jpg", 1000, 1000)]);

        Assert.Equal(DiscoveryCardShape.Square, surface.Shape);
        Assert.Equal(DiscoverySurfaceKind.ArtistPhotoSquare, surface.SurfaceKind);
        Assert.Null(surface.TileImageUrl);
    }
}
