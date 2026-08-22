using MediaEngine.Api.Endpoints;
using MediaEngine.Api.Services.Canonical;
using MediaEngine.Domain.Models;

namespace MediaEngine.Api.Tests;

public sealed class CanonicalCandidateBuilderTests
{
    private static readonly ItemCanonicalEndpoints.CanonicalTargetPolicy AudiobookPolicy = new(
        "Audiobooks",
        "item",
        "audiobook_identity",
        ["title", "author", "narrator"],
        ["series", "year"],
        ["audible_id", "isbn", "asin"],
        ["wikidata_qid"],
        ["title", "author"],
        SearchRetail: true,
        SearchUniverse: true,
        AllowsTextOnly: true);

    [Fact]
    public void RetailAudiobookCandidate_DoesNotRequireNarratorToBeApplicable()
    {
        var candidate = new RetailCandidate
        {
            ProviderId = Guid.NewGuid().ToString(),
            ProviderName = "apple_api",
            ProviderItemId = "1553350212",
            Title = "Dungeon Crawler Carl",
            Author = "Matt Dinniman",
            Year = "2026",
            Confidence = 1,
        };

        var result = CanonicalCandidateBuilder.BuildRetailCandidate(candidate, "Audiobooks", AudiobookPolicy);

        Assert.True(result.IsApplicable);
        Assert.Null(result.BlockedReason);
        Assert.DoesNotContain("narrator", result.RequiredFields.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void RetailCandidate_StillRequiresAStableProviderIdentity()
    {
        var candidate = new RetailCandidate
        {
            ProviderId = Guid.NewGuid().ToString(),
            ProviderName = "apple_api",
            Title = "Dungeon Crawler Carl",
            Author = "Matt Dinniman",
            Confidence = 1,
        };

        var result = CanonicalCandidateBuilder.BuildRetailCandidate(candidate, "Audiobooks", AudiobookPolicy);

        Assert.False(result.IsApplicable);
        Assert.Equal("This result does not include a stable provider item ID.", result.BlockedReason);
    }

    [Fact]
    public void CanonicalAudiobookCandidate_DoesNotRequireNarratorToBeApplicable()
    {
        var candidate = new UniverseCandidate
        {
            Qid = "Q136529136",
            Label = "Dungeon Crawler Carl",
            Author = "Matt Dinniman",
            Year = "2020",
            Confidence = 0.95,
        };

        var result = CanonicalCandidateBuilder.BuildLinkedCandidate(candidate, "Audiobooks", AudiobookPolicy);

        Assert.True(result.IsApplicable);
        Assert.Null(result.BlockedReason);
    }

    [Fact]
    public void CanonicalCandidate_StillRequiresAValidQid()
    {
        var candidate = new UniverseCandidate
        {
            Qid = "not-a-qid",
            Label = "Dungeon Crawler Carl",
            Author = "Matt Dinniman",
            Confidence = 0.95,
        };

        var result = CanonicalCandidateBuilder.BuildLinkedCandidate(candidate, "Audiobooks", AudiobookPolicy);

        Assert.False(result.IsApplicable);
        Assert.Equal("This result does not include a valid Wikidata QID.", result.BlockedReason);
    }

    [Fact]
    public void CanonicalCandidate_KeepsAutomaticResolverConfidenceSeparateFromFieldComparison()
    {
        var candidate = new UniverseCandidate
        {
            Qid = "Q136529136",
            Label = "Dungeon Crawler Carl",
            Author = "Matt Dinniman",
            Confidence = 0.55,
            CoverUrl = "https://retail.example/cover.jpg",
            MatchScores = new FieldMatchResult
            {
                TitleScore = 1,
                AuthorScore = 1,
                YearScore = -1,
                FormatScore = 1,
                CompositeScore = 0.98,
            },
        };

        var result = CanonicalCandidateBuilder.BuildLinkedCandidate(candidate, "Audiobooks", AudiobookPolicy);

        Assert.Equal(0.55, result.Confidence);
        Assert.NotNull(result.MatchScores);
        Assert.Equal(1, result.MatchScores.TitleScore);
        Assert.Null(result.CoverUrl);
    }
}
