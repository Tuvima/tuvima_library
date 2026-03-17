using MediaEngine.Intelligence.Strategies;

namespace MediaEngine.Intelligence.Tests;

/// <summary>
/// Tests for <see cref="ExactMatchStrategy"/> and <see cref="LevenshteinStrategy"/>.
/// </summary>
public sealed class StrategyTests
{
    // ════════════════════════════════════════════════════════════════════════
    //  ExactMatchStrategy
    // ════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("isbn", true)]
    [InlineData("imdbid", true)]
    [InlineData("tmdbid", true)]
    [InlineData("asin", true)]
    [InlineData("ean", true)]
    [InlineData("musicbrainzid", true)]
    [InlineData("openlibrary_id", true)]
    [InlineData("title", false)]
    [InlineData("author", false)]
    [InlineData("year", false)]
    [InlineData("", false)]
    public void ExactMatch_AppliesTo_HardIdentifiersOnly(string key, bool expected)
    {
        var strategy = new ExactMatchStrategy();
        Assert.Equal(expected, strategy.AppliesTo(key));
    }

    [Fact]
    public void ExactMatch_IdenticalISBN_Returns1()
    {
        var strategy = new ExactMatchStrategy();
        Assert.Equal(1.0, strategy.Compute("9780441172719", "9780441172719"));
    }

    [Fact]
    public void ExactMatch_DifferentISBN_Returns0()
    {
        var strategy = new ExactMatchStrategy();
        Assert.Equal(0.0, strategy.Compute("9780441172719", "9780547928210"));
    }

    // ── ISBN normalization: strips hyphens, whitespace, prefixes ─────────────

    [Theory]
    [InlineData("978-0-441-17271-9", "9780441172719")]
    [InlineData("ISBN:978-0-441-17271-9", "9780441172719")]
    [InlineData("urn:isbn:9780441172719", "9780441172719")]
    [InlineData("isbn:9780441172719", "ISBN 9780441172719")]
    public void ExactMatch_NormalizesISBN(string a, string b)
    {
        var strategy = new ExactMatchStrategy();
        Assert.Equal(1.0, strategy.Compute(a, b));
    }

    // ── IMDb ID normalization ────────────────────────────────────────────────

    [Theory]
    [InlineData("tt1160419", "imdb:tt1160419")]
    [InlineData("tt1160419", "1160419")]
    public void ExactMatch_NormalizesImdbId(string a, string b)
    {
        var strategy = new ExactMatchStrategy();
        Assert.Equal(1.0, strategy.Compute(a, b));
    }

    // ── Empty values ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("", "9780441172719")]
    [InlineData("9780441172719", "")]
    [InlineData("", "")]
    [InlineData(null, "9780441172719")]
    public void ExactMatch_EmptyOrNull_Returns0(string? a, string? b)
    {
        var strategy = new ExactMatchStrategy();
        Assert.Equal(0.0, strategy.Compute(a!, b!));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  FuzzyMatchingService (replaced LevenshteinStrategy)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Fuzzy_IdenticalStrings_Returns1()
    {
        var fuzzy = new MediaEngine.Intelligence.Services.FuzzyMatchingService();
        Assert.Equal(1.0, fuzzy.ComputeTokenSetRatio("Dune", "Dune"));
    }

    [Fact]
    public void Fuzzy_CaseInsensitive()
    {
        var fuzzy = new MediaEngine.Intelligence.Services.FuzzyMatchingService();
        Assert.Equal(1.0, fuzzy.ComputeTokenSetRatio("The Hobbit", "the hobbit"));
    }

    [Fact]
    public void Fuzzy_SimilarStrings_HighScore()
    {
        var fuzzy = new MediaEngine.Intelligence.Services.FuzzyMatchingService();
        double score = fuzzy.ComputeTokenSetRatio("The Lord of the Rings", "Lord of the Rings");
        Assert.True(score > 0.8);
    }

    [Fact]
    public void Fuzzy_CompletelyDifferent_LowScore()
    {
        var fuzzy = new MediaEngine.Intelligence.Services.FuzzyMatchingService();
        double score = fuzzy.ComputeTokenSetRatio("Dune", "War and Peace");
        Assert.True(score < 0.5);
    }

    [Fact]
    public void Fuzzy_EmptyString_Returns0()
    {
        var fuzzy = new MediaEngine.Intelligence.Services.FuzzyMatchingService();
        Assert.Equal(0.0, fuzzy.ComputeTokenSetRatio("", "something"));
        Assert.Equal(0.0, fuzzy.ComputeTokenSetRatio("something", ""));
    }

    [Fact]
    public void Fuzzy_ReorderedWords_HighScore()
    {
        var fuzzy = new MediaEngine.Intelligence.Services.FuzzyMatchingService();
        double score = fuzzy.ComputeTokenSetRatio("Herbert, Frank", "Frank Herbert");
        Assert.True(score >= 0.95);
    }
}
