using System.Text.Json;
using MediaEngine.Contracts.Authentication;
using MediaEngine.Contracts.Playback;

namespace MediaEngine.Api.Tests;

public sealed class NativeClientContractFixtureTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void CheckedInV1Fixtures_MatchThePublishedContractTypes()
    {
        var discovery = Read<TuvimaDiscoveryResponse>("discovery.json");
        var authorization = Read<DeviceAuthorizationResponse>("device-authorization.json");
        var token = Read<OAuthTokenResponse>("oauth-token.json");
        var manifest = Read<PlaybackManifestDto>("playback-manifest-hls.json");

        Assert.Contains("1", discovery.SupportedApiVersions);
        Assert.Equal(600, authorization.ExpiresIn);
        Assert.Equal("Bearer", token.TokenType);
        Assert.Equal(PlaybackDeliveryModes.Hls, manifest.RecommendedDelivery);
        Assert.Equal("ready", manifest.HlsStatus);
        Assert.NotNull(manifest.Resume);
    }

    [Fact]
    public void CheckedInV1Fixtures_NeverContainSecretsOrServerPaths()
    {
        foreach (var path in Directory.EnumerateFiles(FixtureDirectory(), "*.json"))
        {
            var contents = File.ReadAllText(path);
            Assert.DoesNotContain("X-Tuvima-Service-Key", contents, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("outputPath", contents, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(@"C:\", contents, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static T Read<T>(string fileName) where T : class =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(FixtureDirectory(), fileName)), JsonOptions)
        ?? throw new InvalidOperationException($"Fixture '{fileName}' did not deserialize as {typeof(T).Name}.");

    private static string FixtureDirectory() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "tests", "fixtures", "native-client-v1"));
}
