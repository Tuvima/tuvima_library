using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using MediaEngine.Domain;
using MediaEngine.Domain.Models;
using MediaEngine.Providers.Models;
using MediaEngine.Providers.Workers;

namespace MediaEngine.Providers.Tests;

public sealed class SequenceManifestProjectionTests
{
    [Fact]
    public void TmdbSeasonManifest_PreservesExactEpisodeMetadataAndCountKind()
    {
        var episodes = new List<JsonNode>
        {
            JsonNode.Parse("""
                {
                  "id": 101,
                  "episode_number": 1,
                  "name": "Pilot",
                  "air_date": "2024-01-15",
                  "runtime": 54,
                  "overview": "The story begins."
                }
                """)!,
            JsonNode.Parse("""
                {
                  "id": 102,
                  "episode_number": 2,
                  "name": "Second",
                  "air_date": "2024-01-22",
                  "runtime": 51,
                  "overview": "The story continues."
                }
                """)!,
        };
        var method = typeof(RetailMatchWorker).GetMethod(
            "BuildTmdbSeasonManifestClaims",
            BindingFlags.Static | BindingFlags.NonPublic);

        var claims = Assert.IsAssignableFrom<IReadOnlyList<ProviderClaim>>(
            method!.Invoke(null, [episodes, "900", "Test Show", "1"]));
        var manifestClaim = Assert.Single(claims);
        var manifest = JsonSerializer.Deserialize<ProviderSequenceManifest>(manifestClaim.Value);

        Assert.NotNull(manifest);
        Assert.True(manifest.IsAuthoritative);
        Assert.Equal("TvSeason", manifest.ContainerKind);
        Assert.Equal("episodes", manifest.ExpectedTotalKind);
        Assert.Equal(2, manifest.ExpectedTotal);
        Assert.Equal("2024-01-15", manifest.Items[0].ReleaseDate);
        Assert.Equal("54", manifest.Items[0].Duration);
        Assert.Equal("The story begins.", manifest.Items[0].Description);
    }

    [Fact]
    public void TmdbEpisodeClaims_ProjectExactAirDateAndEpisodeIdentity()
    {
        var episode = JsonNode.Parse("""
            {
              "id": 101,
              "episode_number": 1,
              "season_number": 1,
              "name": "Pilot",
              "air_date": "2024-01-15",
              "runtime": 54,
              "overview": "The story begins."
            }
            """)!;
        var method = typeof(RetailMatchWorker).GetMethod(
            "BuildTvEpisodeClaims",
            BindingFlags.Static | BindingFlags.NonPublic);

        var claims = Assert.IsAssignableFrom<IReadOnlyList<ProviderClaim>>(
            method!.Invoke(null, [episode, "900", "Test Show", "1", null]));

        Assert.Contains(claims, claim => claim.Key == MetadataFieldConstants.AirDate && claim.Value == "2024-01-15");
        Assert.Contains(claims, claim => claim.Key == BridgeIdKeys.TmdbEpisodeId && claim.Value == "101");
        Assert.Contains(claims, claim => claim.Key == MetadataFieldConstants.Runtime && claim.Value == "54");
    }
}
