using MediaEngine.Web.Services.Integration;

namespace MediaEngine.Web.Tests;

public sealed class EngineImageProxyPathTests
{
    private static readonly Uri EngineBaseAddress = new("http://localhost:61495");

    [Theory]
    [InlineData("/stream/artwork/11111111-1111-1111-1111-111111111111?size=s", "/engine-image/stream/artwork/11111111-1111-1111-1111-111111111111?size=s")]
    [InlineData("/stream/22222222-2222-2222-2222-222222222222/cover", "/engine-image/stream/22222222-2222-2222-2222-222222222222/cover")]
    [InlineData("http://localhost:61495/persons/33333333-3333-3333-3333-333333333333/headshot", "/engine-image/persons/33333333-3333-3333-3333-333333333333/headshot")]
    public void ToBrowserUrl_ProxiesKnownEngineImages(string value, string expected)
    {
        Assert.Equal(expected, EngineImageProxyPath.ToBrowserUrl(value, EngineBaseAddress));
    }

    [Theory]
    [InlineData("/stream/11111111-1111-1111-1111-111111111111")]
    [InlineData("/stream/11111111-1111-1111-1111-111111111111/lyrics")]
    [InlineData("/auth/sessions")]
    [InlineData("/stream/artwork/not-a-guid")]
    public void IsAllowedEnginePath_RejectsNonArtworkRoutes(string value)
    {
        Assert.False(EngineImageProxyPath.IsAllowedEnginePath(value));
    }

    [Fact]
    public void ToBrowserUrl_DoesNotProxyAnotherOrigin()
    {
        const string remote = "https://images.example.test/stream/artwork/11111111-1111-1111-1111-111111111111";
        Assert.Equal(remote, EngineImageProxyPath.ToBrowserUrl(remote, EngineBaseAddress));
    }
}
