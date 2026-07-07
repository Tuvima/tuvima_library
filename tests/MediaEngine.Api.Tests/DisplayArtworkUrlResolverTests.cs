using MediaEngine.Api.Services.Display;

namespace MediaEngine.Api.Tests;

public sealed class DisplayArtworkUrlResolverTests
{
    [Fact]
    public void Resolve_ProxiesExternalProviderUrlThroughStream_WhenArtworkHasNotSettled()
    {
        var assetId = Guid.NewGuid();

        var result = DisplayArtworkUrlResolver.Resolve(
            "https://is1-ssl.mzstatic.com/image/thumb/Music/test.jpg",
            assetId,
            "cover",
            state: null);

        Assert.Equal($"/stream/{assetId:D}/cover", result);
    }

    [Fact]
    public void Resolve_UsesStreamUrlForExternalProviderUrl_WhenArtworkIsPresent()
    {
        var assetId = Guid.NewGuid();

        var result = DisplayArtworkUrlResolver.Resolve(
            "https://image.tmdb.org/t/p/original/poster.jpg",
            assetId,
            "cover",
            state: "present");

        Assert.Equal($"/stream/{assetId:D}/cover", result);
    }

    [Fact]
    public void Resolve_PreservesManagedArtworkUrl()
    {
        var result = DisplayArtworkUrlResolver.Resolve(
            "/stream/artwork/11111111-1111-1111-1111-111111111111?size=s",
            Guid.NewGuid(),
            "cover",
            state: "present");

        Assert.Equal("/stream/artwork/11111111-1111-1111-1111-111111111111?size=s", result);
    }
}
