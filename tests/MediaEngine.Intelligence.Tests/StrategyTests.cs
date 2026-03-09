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
    //  LevenshteinStrategy
    // ════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("title", true)]
    [InlineData("author", true)]
    [InlineData("year", true)]
    [InlineData("isbn", false)]    // excluded — handled by ExactMatch
    [InlineData("imdbid", false)]
    [InlineData("asin", false)]
    public void Levenshtein_AppliesTo_FreeTextOnly(string key, bool expected)
    {
        var strategy = new LevenshteinStrategy();
        Assert.Equal(expected, strategy.AppliesTo(key));
    }

    [Fact]
    public void Levenshtein_IdenticalStrings_Returns1()
    {
        var strategy = new LevenshteinStrategy();
        Assert.Equal(1.0, strategy.Compute("Dune", "Dune"));
    }

    [Fact]
    public void Levenshtein_CaseInsensitive()
    {
        var strategy = new LevenshteinStrategy();
        Assert.Equal(1.0, strategy.Compute("The Hobbit", "the hobbit"));
    }

    [Fact]
    public void Levenshtein_SimilarStrings_HighScore()
    {
        var strategy = new LevenshteinStrategy();
        double score = strategy.Compute("The Lord of the Rings", "Lord of the Rings");

        // Very similar — should be high but not 1.0.
        Assert.True(score > 0.8);
        Assert.True(score < 1.0);
    }

    [Fact]
    public void Levenshtein_CompletelyDifferent_LowScore()
    {
        var strategy = new LevenshteinStrategy();
        double score = strategy.Compute("Dune", "War and Peace");

        Assert.True(score < 0.3);
    }

    [Theory]
    [InlineData("", "")]
    public void Levenshtein_BothEmpty_Returns1(string a, string b)
    {
        var strategy = new LevenshteinStrategy();
        Assert.Equal(1.0, strategy.Compute(a, b));
    }

    [Theory]
    [InlineData("", "something")]
    [InlineData("something", "")]
    public void Levenshtein_OneEmpty_Returns0(string a, string b)
    {
        var strategy = new LevenshteinStrategy();
        Assert.Equal(0.0, strategy.Compute(a, b));
    }

    [Fact]
    public void Levenshtein_SingleCharDifference()
    {
        var strategy = new LevenshteinStrategy();
        double score = strategy.Compute("cat", "bat");

        // 1 edit out of 3 chars → similarity = 1 - 1/3 ≈ 0.667.
        Assert.True(Math.Abs(score - (2.0 / 3.0)) < 0.01);
    }
}
