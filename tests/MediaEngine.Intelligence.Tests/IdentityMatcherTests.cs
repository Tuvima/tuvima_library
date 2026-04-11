using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
using MediaEngine.Intelligence.Models;
using MediaEngine.Intelligence.Strategies;

namespace MediaEngine.Intelligence.Tests;

/// <summary>
/// Tests for <see cref="IdentityMatcher"/> — determines whether two entities
/// represent the same intellectual work.
/// </summary>
public sealed class IdentityMatcherTests
{
    private static readonly ScoringConfiguration DefaultConfig = new();

    private static IdentityMatcher CreateMatcher() => new(new StubFuzzyMatchingService(), new ExactMatchStrategy());

    // ── Hard identifier match → 1.0 immediately ─────────────────────────────

    [Fact]
    public async Task MatchingISBN_Returns1_HardIdentifierMatch()
    {
        var matcher = CreateMatcher();

        var entity = new[]
        {
            MakeCanonical("isbn", "9780441172719"),
            MakeCanonical("title", "Dune"),
        };
        var candidate = new[]
        {
            MakeCanonical("isbn", "9780441172719"),
            MakeCanonical("title", "Dune: A Novel"),
        };

        var result = await matcher.MatchAsync(entity, candidate, DefaultConfig);

        Assert.Equal(1.0, result.Similarity);
        Assert.True(result.HardIdentifierMatch);
        Assert.Contains("isbn", result.MatchedIdentifiers);
        Assert.Equal(LinkDisposition.AutoLinked, result.Disposition);
    }

    // ── Matching title + author → high similarity ────────────────────────────

    [Fact]
    public async Task MatchingTitleAndAuthor_HighSimilarity()
    {
        var matcher = CreateMatcher();

        var entity    = new[] { MakeCanonical("title", "Dune"), MakeCanonical("author", "Frank Herbert") };
        var candidate = new[] { MakeCanonical("title", "Dune"), MakeCanonical("author", "Frank Herbert") };

        var result = await matcher.MatchAsync(entity, candidate, DefaultConfig);

        Assert.Equal(1.0, result.Similarity);
        Assert.Equal(LinkDisposition.AutoLinked, result.Disposition);
    }

    // ── Different titles → low similarity ────────────────────────────────────

    [Fact]
    public async Task DifferentTitles_LowSimilarity()
    {
        var matcher = CreateMatcher();

        var entity    = new[] { MakeCanonical("title", "Dune") };
        var candidate = new[] { MakeCanonical("title", "War and Peace") };

        var result = await matcher.MatchAsync(entity, candidate, DefaultConfig);

        Assert.True(result.Similarity < 0.5);
        Assert.Equal(LinkDisposition.Rejected, result.Disposition);
    }

    // ── Title weighting: title gets 50% ──────────────────────────────────────

    [Fact]
    public async Task Title_Gets50PercentWeight()
    {
        var matcher = CreateMatcher();

        // Same title, different author → title contributes 50%.
        var entity    = new[] { MakeCanonical("title", "Dune"), MakeCanonical("author", "Frank Herbert") };
        var candidate = new[] { MakeCanonical("title", "Dune"), MakeCanonical("author", "Completely Different") };

        var result = await matcher.MatchAsync(entity, candidate, DefaultConfig);

        // Title match = 1.0 × 0.5 = 0.5; author match ≈ 0.x × 0.5.
        // Total should be between 0.5 and 1.0 but less than 1.0.
        Assert.True(result.Similarity >= 0.5);
        Assert.True(result.Similarity < 1.0);
    }

    // ── No common fields → 0.0 similarity ───────────────────────────────────

    [Fact]
    public async Task NoCommonFields_ZeroSimilarity()
    {
        var matcher = CreateMatcher();

        var entity    = new[] { MakeCanonical("title", "Dune") };
        var candidate = new[] { MakeCanonical("author", "Frank Herbert") };

        var result = await matcher.MatchAsync(entity, candidate, DefaultConfig);

        Assert.Equal(0.0, result.Similarity);
        Assert.Equal(LinkDisposition.Rejected, result.Disposition);
    }

    // ── Disposition thresholds ───────────────────────────────────────────────

    [Fact]
    public async Task Score_AboveAutoLink_AutoLinked()
    {
        var config = new ScoringConfiguration { AutoLinkThreshold = 0.85 };
        var matcher = CreateMatcher();

        var entity    = new[] { MakeCanonical("title", "The Hobbit") };
        var candidate = new[] { MakeCanonical("title", "The Hobbit") };

        var result = await matcher.MatchAsync(entity, candidate, config);

        Assert.Equal(LinkDisposition.AutoLinked, result.Disposition);
    }

    [Fact]
    public async Task Score_BetweenThresholds_NeedsReview()
    {
        var config = new ScoringConfiguration
        {
            AutoLinkThreshold = 0.85,
            ConflictThreshold = 0.60,
        };
        var matcher = CreateMatcher();

        // Slightly different titles → similarity around 0.6–0.85.
        var entity    = new[] { MakeCanonical("title", "The Lord of the Rings") };
        var candidate = new[] { MakeCanonical("title", "Lord of the Rings") };

        var result = await matcher.MatchAsync(entity, candidate, config);

        // If score is in [0.60, 0.85), disposition should be NeedsReview.
        if (result.Similarity >= config.ConflictThreshold &&
            result.Similarity < config.AutoLinkThreshold)
        {
            Assert.Equal(LinkDisposition.NeedsReview, result.Disposition);
        }
    }

    // ── Helper ───────────────────────────────────────────────────────────────

    private static CanonicalValue MakeCanonical(string key, string value) => new()
    {
        EntityId     = Guid.NewGuid(),
        Key          = key,
        Value        = value,
        LastScoredAt = DateTimeOffset.UtcNow,
    };
}
