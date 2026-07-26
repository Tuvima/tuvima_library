using MediaEngine.Providers.Services;

namespace MediaEngine.Providers.Tests;

/// <summary>
/// Characterization tests for <see cref="RetailTextSimilarity.NormalizeComparableText"/> —
/// the canonical comparable-text normalization shared by Stage 1 retail matching and
/// Stage 2 Wikidata bridging. These tests pin the exact current behavior (diacritic
/// folding, ampersand mapping, punctuation collapse, whitespace collapse) so that the
/// method can be safely promoted from private to public and every other copy in
/// <c>MediaEngine.Providers</c> can be deleted in favor of calling this one directly,
/// without silently changing what "equivalent text" means anywhere in the pipeline.
/// </summary>
public sealed class RetailTextSimilarityTests
{
    [Theory]
    [InlineData("Für Elise & Co", "fur elise and co")]
    [InlineData("Café", "cafe")]
    [InlineData("Beyoncé", "beyonce")]
    [InlineData("Rock & Roll", "rock and roll")]
    [InlineData("  The   HOBBIT  ", "the hobbit")]
    [InlineData("Spider-Man: No Way Home", "spider man no way home")]
    public void NormalizeComparableText_KnownInputs_ProducesExpectedCanonicalForm(string input, string expected)
    {
        Assert.Equal(expected, RetailTextSimilarity.NormalizeComparableText(input));
    }

    [Fact]
    public void NormalizeComparableText_EmptyString_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, RetailTextSimilarity.NormalizeComparableText(string.Empty));
    }

    [Fact]
    public void NormalizeComparableText_WhitespaceOnly_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, RetailTextSimilarity.NormalizeComparableText("   "));
    }

    [Fact]
    public void NormalizeComparableText_NullInput_ThrowsNullReferenceException()
    {
        // The method does not null-check its argument — callers (e.g. AreEquivalentNames)
        // are responsible for guarding with IsNullOrWhiteSpace before calling. This test
        // pins that existing contract so promoting the method to public doesn't silently
        // change null-handling behavior.
        string? nullText = null;
        Assert.Throws<NullReferenceException>(() => RetailTextSimilarity.NormalizeComparableText(nullText!));
    }
}
