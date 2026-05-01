using System.Text.Json;
using MediaEngine.Domain;
using MediaEngine.Providers.Adapters;
using Tuvima.Wikidata;

namespace MediaEngine.Providers.Tests;

public sealed class ReconciliationAdapterContractTests
{
    [Fact]
    public void BuildTvManifestProjection_NestsEpisodesByParentSeason()
    {
        var manifest = new ChildEntityManifest
        {
            ParentQid = "Q100",
            PrimaryCount = 2,
            TotalCount = 4,
            Children =
            [
                new ChildEntityRef { Qid = "QSeason1", Title = "Season 1", Ordinal = 1 },
                new ChildEntityRef { Qid = "QSeason2", Title = "Season 2", Ordinal = 2 },
                new ChildEntityRef
                {
                    Qid = "QEpisode1",
                    Title = "Pilot",
                    Ordinal = 1,
                    Parent = 1,
                    ReleaseDate = new DateOnly(2008, 1, 20),
                    Duration = TimeSpan.FromMinutes(58),
                    Creators = new Dictionary<string, string> { ["Director"] = "Vince Gilligan" },
                },
                new ChildEntityRef
                {
                    Qid = "QEpisode2",
                    Title = "Next",
                    Ordinal = 2,
                    Parent = 2,
                    ReleaseDate = new DateOnly(2009, 3, 8),
                    Duration = TimeSpan.FromMinutes(47),
                    Creators = new Dictionary<string, string> { ["Director"] = "Michelle MacLaren" },
                },
            ],
        };

        var projection = ReconciliationAdapter.BuildTvManifestProjection(
            manifest,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["QEpisode1"] = "Walter White starts cooking methamphetamine after his cancer diagnosis.",
            });

        Assert.Equal(2, projection.SeasonCount);
        Assert.Equal(2, projection.EpisodeCount);
        Assert.Equal(0, projection.UnassignedEpisodeCount);

        using var json = JsonDocument.Parse(projection.JsonBlob);
        var seasons = json.RootElement.GetProperty("seasons");
        Assert.Equal(2, seasons.GetArrayLength());

        var firstSeason = seasons[0];
        Assert.Equal("QSeason1", firstSeason.GetProperty("qid").GetString());
        Assert.Equal("Season 1", firstSeason.GetProperty("label").GetString());
        Assert.Equal(1, firstSeason.GetProperty("season_number").GetInt32());

        var firstEpisode = firstSeason.GetProperty("episodes")[0];
        Assert.Equal("QEpisode1", firstEpisode.GetProperty("qid").GetString());
        Assert.Equal("Pilot", firstEpisode.GetProperty("title").GetString());
        Assert.Equal(1, firstEpisode.GetProperty("episode_number").GetInt32());
        Assert.Equal(
            "Walter White starts cooking methamphetamine after his cancer diagnosis.",
            firstEpisode.GetProperty("description").GetString());
        Assert.Equal("2008-01-20", firstEpisode.GetProperty("air_date").GetString());
        Assert.Equal(58, firstEpisode.GetProperty("duration_minutes").GetInt32());
        Assert.Equal("Vince Gilligan", firstEpisode.GetProperty("director").GetString());

        var secondSeason = seasons[1];
        Assert.Equal("QSeason2", secondSeason.GetProperty("qid").GetString());
        Assert.Equal("QEpisode2", secondSeason.GetProperty("episodes")[0].GetProperty("qid").GetString());
    }

    [Fact]
    public void BuildTvManifestProjection_PutsParentlessEpisodesInUnassignedBucket()
    {
        var manifest = new ChildEntityManifest
        {
            ParentQid = "Q200",
            PrimaryCount = 1,
            TotalCount = 2,
            Children =
            [
                new ChildEntityRef { Qid = "QSeason1", Title = "Season 1", Ordinal = 1 },
                new ChildEntityRef
                {
                    Qid = "QEpisodeOrphan",
                    Title = "Lost Episode",
                    Ordinal = 7,
                    ReleaseDate = new DateOnly(2010, 4, 10),
                },
            ],
        };

        var projection = ReconciliationAdapter.BuildTvManifestProjection(manifest);

        Assert.Equal(1, projection.SeasonCount);
        Assert.Equal(1, projection.EpisodeCount);
        Assert.Equal(1, projection.UnassignedEpisodeCount);

        using var json = JsonDocument.Parse(projection.JsonBlob);
        var seasons = json.RootElement.GetProperty("seasons");
        Assert.Equal(2, seasons.GetArrayLength());

        var unassignedSeason = seasons[1];
        Assert.Equal("Unassigned", unassignedSeason.GetProperty("label").GetString());
        Assert.Equal(JsonValueKind.Null, unassignedSeason.GetProperty("qid").ValueKind);
        Assert.Equal("QEpisodeOrphan", unassignedSeason.GetProperty("episodes")[0].GetProperty("qid").GetString());
    }

    [Fact]
    public void BuildResolvedAuthorPseudonymClaims_MapsPattern1AndPattern2Claims()
    {
        var resolution = new AuthorResolutionResult
        {
            Authors =
            [
                new ResolvedAuthor
                {
                    OriginalName = "Richard Bachman",
                    Qid = "Q39829",
                    RealNameQid = "Q39829",
                },
                new ResolvedAuthor
                {
                    OriginalName = "Stephen King",
                    Qid = "Q39829",
                    CanonicalName = "Stephen King",
                    Pseudonyms = ["Richard Bachman", "John Swithen"],
                },
            ],
        };

        var claims = ReconciliationAdapter.BuildResolvedAuthorPseudonymClaims(resolution);

        Assert.Equal(3, claims.Count);
        Assert.Contains(claims, c => c.Key == BridgeIdKeys.AuthorRealNameQid && c.Value == "Q39829");
        Assert.Equal(
            2,
            claims.Count(c => c.Key == BridgeIdKeys.AuthorPseudonym));
        Assert.Contains(claims, c => c.Key == BridgeIdKeys.AuthorPseudonym && c.Value == "Richard Bachman");
        Assert.Contains(claims, c => c.Key == BridgeIdKeys.AuthorPseudonym && c.Value == "John Swithen");
    }

    [Fact]
    public void BuildResolvedAuthorPseudonymClaims_IgnoresPattern3RealAuthors()
    {
        var resolution = new AuthorResolutionResult
        {
            Authors =
            [
                new ResolvedAuthor
                {
                    OriginalName = "James S.A. Corey",
                    Qid = "Q6142591",
                    RealAuthors =
                    [
                        new RealAuthor { Qid = "Q3122844", CanonicalName = "Daniel Abraham" },
                        new RealAuthor { Qid = "Q3052772", CanonicalName = "Ty Franck" },
                    ],
                },
            ],
        };

        var claims = ReconciliationAdapter.BuildResolvedAuthorPseudonymClaims(resolution);

        Assert.Empty(claims);
    }
}
