using MediaEngine.Web.Services.MediaTiles;

namespace MediaEngine.Web.Tests;

public sealed class ArtworkImageDeliveryTests
{
    [Theory]
    [InlineData("/stream/artwork/11111111-1111-1111-1111-111111111111?size=l", "/stream/artwork/11111111-1111-1111-1111-111111111111?size=s")]
    [InlineData("/api/v1/display/artwork/assets/11111111-1111-1111-1111-111111111111/content?size=l", "/api/v1/display/artwork/assets/11111111-1111-1111-1111-111111111111/content?size=s")]
    [InlineData("/stream/entity/tvshow/11111111-1111-1111-1111-111111111111/cover?size=l", "/stream/entity/tvshow/11111111-1111-1111-1111-111111111111/cover?size=s")]
    [InlineData("/persons/11111111-1111-1111-1111-111111111111/headshot?size=l", "/persons/11111111-1111-1111-1111-111111111111/headshot?size=s")]
    public void Sized_UsesTheRequestedRenditionForSupportedArtworkRoutes(string source, string expected)
    {
        Assert.Equal(expected, MediaTileArtworkUrl.Sized(source, "s"));
    }

    [Fact]
    public void Sized_PreservesNonSizeQueryParametersAndFragments()
    {
        var result = MediaTileArtworkUrl.Sized(
            "/stream/entity/work/11111111-1111-1111-1111-111111111111/cover?token=abc&size=l#preview",
            "m");

        Assert.Equal(
            "/stream/entity/work/11111111-1111-1111-1111-111111111111/cover?token=abc&size=m#preview",
            result);
    }

    [Fact]
    public void Sized_RejectsRoutesWithoutAnAcceptedRenditionContract()
    {
        Assert.Null(MediaTileArtworkUrl.Sized("https://images.example.test/original.jpg", "s"));
        Assert.Null(MediaTileArtworkUrl.Sized("/collections/11111111-1111-1111-1111-111111111111/artwork/poster", "s"));
    }
}
