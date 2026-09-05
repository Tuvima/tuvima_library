using MediaEngine.Api.Services.Metadata;
using MediaEngine.Domain;
using MediaEngine.Domain.Constants;

namespace MediaEngine.Api.Tests;

public sealed class ArtworkScopeProviderBridgeTests
{
    [Fact]
    public void MusicAlbum_UsesReleaseGroupWithoutRequiringWikidataIdentity()
    {
        var bridge = ArtworkScopeService.ResolveProviderArtworkBridge(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [BridgeIdKeys.MusicBrainzReleaseGroupId] = "release-group-1",
            },
            "Music");

        Assert.Equal(
            (BridgeIdKeys.MusicBrainzReleaseGroupId, "release-group-1"),
            bridge);
    }

    [Fact]
    public void Movie_UsesTmdbIdentityWithoutRequiringWikidataIdentity()
    {
        var bridge = ArtworkScopeService.ResolveProviderArtworkBridge(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["tmdb_movie_id"] = "1234",
            },
            "Movies");

        Assert.Equal(("tmdb_movie_id", "1234"), bridge);
    }
}
