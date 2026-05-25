using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.MediaTiles;

namespace MediaEngine.Web.Tests;

public sealed class MediaTileArtworkResolverTests
{
    [Fact]
    public void Resolve_LandscapeDimensionsProduceLandscapeTileAndHover()
    {
        var surface = MediaTileArtworkResolver.Resolve(
            MediaTileBucket.Movie,
            MediaTilePresentation.Default,
            [new MediaTileArtworkVariant(ArtworkRole.Background, "/art/backdrop-s.jpg", "/art/backdrop-m.jpg", "/art/backdrop-l.jpg", 1920, 1080)]);

        Assert.Equal(MediaTileShape.Landscape, surface.Shape);
        Assert.Equal(MediaTileSurfaceKind.BannerLandscape, surface.SurfaceKind);
        Assert.Equal(MediaTileHoverLayout.BannerPopover, surface.HoverLayout);
        Assert.Equal("/art/backdrop-s.jpg", surface.TileImageUrl);
        Assert.Equal("/art/backdrop-s.jpg 320w, /art/backdrop-m.jpg 960w", surface.TileImageSrcSet);
        Assert.Equal("/art/backdrop-m.jpg", surface.HoverImageUrl);
        Assert.Equal("/art/backdrop-m.jpg 960w, /art/backdrop-l.jpg 2160w", surface.HoverImageSrcSet);
    }

    [Fact]
    public void Resolve_SquareDimensionsProduceSquareTileAndHover()
    {
        var surface = MediaTileArtworkResolver.Resolve(
            MediaTileBucket.Music,
            MediaTilePresentation.Album,
            [new MediaTileArtworkVariant(ArtworkRole.Cover, "/art/album-s.jpg", "/art/album-m.jpg", "/art/album-l.jpg", 1000, 1000)]);

        Assert.Equal(MediaTileShape.Square, surface.Shape);
        Assert.Equal(MediaTileSurfaceKind.CoverSquare, surface.SurfaceKind);
        Assert.Equal(MediaTileHoverLayout.ArtOnlyPopover, surface.HoverLayout);
    }

    [Fact]
    public void Resolve_PortraitCoverDimensionsProducePortraitTileAndHover()
    {
        var surface = MediaTileArtworkResolver.Resolve(
            MediaTileBucket.Book,
            MediaTilePresentation.Default,
            [new MediaTileArtworkVariant(ArtworkRole.Cover, "/art/book-s.jpg", "/art/book-m.jpg", "/art/book-l.jpg", 600, 900)]);

        Assert.Equal(MediaTileShape.Portrait, surface.Shape);
        Assert.Equal(MediaTileSurfaceKind.CoverPortrait, surface.SurfaceKind);
        Assert.Equal(MediaTileHoverLayout.ArtOnlyPopover, surface.HoverLayout);
    }

    [Fact]
    public void Resolve_MovieCoverOnlyDoesNotBecomeLandscape()
    {
        var surface = MediaTileArtworkResolver.Resolve(
            MediaTileBucket.Movie,
            MediaTilePresentation.Default,
            [new MediaTileArtworkVariant(ArtworkRole.Cover, "/art/poster-s.jpg", "/art/poster-m.jpg", "/art/poster-l.jpg", 600, 900)]);

        Assert.Equal(MediaTileShape.Portrait, surface.Shape);
        Assert.Equal(MediaTileSurfaceKind.CoverPortrait, surface.SurfaceKind);
        Assert.Equal("/art/poster-s.jpg", surface.TileImageUrl);
    }

    [Fact]
    public void Resolve_ArtistDoesNotFallBackToAlbumCover()
    {
        var surface = MediaTileArtworkResolver.Resolve(
            MediaTileBucket.Music,
            MediaTilePresentation.Artist,
            [new MediaTileArtworkVariant(ArtworkRole.Cover, "/art/album-s.jpg", "/art/album-m.jpg", "/art/album-l.jpg", 1000, 1000)]);

        Assert.Equal(MediaTileShape.Square, surface.Shape);
        Assert.Equal(MediaTileSurfaceKind.ArtistPhotoSquare, surface.SurfaceKind);
        Assert.Null(surface.TileImageUrl);
    }

    [Fact]
    public void Resolve_DoesNotUseBaseOriginalWhenSizedArtworkIsMissing()
    {
        var surface = MediaTileArtworkResolver.Resolve(
            MediaTileBucket.Book,
            MediaTilePresentation.Default,
            [new MediaTileArtworkVariant(ArtworkRole.Cover, WidthPx: 600, HeightPx: 900)]);

        Assert.Equal(MediaTileShape.Portrait, surface.Shape);
        Assert.Null(surface.TileImageUrl);
        Assert.Null(surface.TileImageSrcSet);
        Assert.Null(surface.HoverImageUrl);
    }
}
