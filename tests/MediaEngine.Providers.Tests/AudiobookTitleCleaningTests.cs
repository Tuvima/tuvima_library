using System.Reflection;
using MediaEngine.Providers.Adapters;

namespace MediaEngine.Providers.Tests;

/// <summary>
/// Tests for the audiobook title cleaning logic in <see cref="ReconciliationAdapter"/>.
/// Uses reflection to access the private <c>CleanAudiobookTitle</c> method.
/// </summary>
public sealed class AudiobookTitleCleaningTests
{
    private static readonly MethodInfo CleanMethod = typeof(ReconciliationAdapter)
        .GetMethod("CleanAudiobookTitle", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("CleanAudiobookTitle method not found on ReconciliationAdapter");

    private static string Clean(string title) =>
        (string)CleanMethod.Invoke(null, [title])!;

    // ── Parenthesized / bracketed suffixes ────────────────────────────────────

    [Theory]
    [InlineData("Project Hail Mary (Unabridged)", "Project Hail Mary")]
    [InlineData("Project Hail Mary [Unabridged]", "Project Hail Mary")]
    [InlineData("Project Hail Mary (Abridged)",   "Project Hail Mary")]
    [InlineData("Project Hail Mary (Audiobook)",  "Project Hail Mary")]
    public void CleanAudiobookTitle_RemovesParenthesizedAudiobookSuffixes(string input, string expected)
    {
        Assert.Equal(expected, Clean(input));
    }

    // ── Subtitle patterns with separator ─────────────────────────────────────

    [Theory]
    [InlineData("Dune: A Novel",                  "Dune")]
    [InlineData("Dune - A Novel",                  "Dune")]
    [InlineData("The Da Vinci Code: A Thriller",   "The Da Vinci Code")]
    [InlineData("Educated: A Memoir",              "Educated")]
    [InlineData("Becoming - A Memoir",             "Becoming")]
    public void CleanAudiobookTitle_RemovesSubtitleSuffixesWithSeparator(string input, string expected)
    {
        Assert.Equal(expected, Clean(input));
    }

    // ── "A Novel" / "A Memoir" without separator ──────────────────────────────

    [Theory]
    [InlineData("Where the Crawdads Sing A Novel", "Where the Crawdads Sing")]
    [InlineData("Homegoing A Novel",               "Homegoing")]
    public void CleanAudiobookTitle_RemovesTrailingNovelMemoirWithoutSeparator(string input, string expected)
    {
        Assert.Equal(expected, Clean(input));
    }

    // ── Titles that must NOT be modified ─────────────────────────────────────

    [Theory]
    [InlineData("The Great Gatsby")]
    [InlineData("Dune")]
    [InlineData("Foundation")]
    public void CleanAudiobookTitle_LeavesCleanTitlesUnchanged(string input)
    {
        Assert.Equal(input, Clean(input));
    }

    [Fact]
    public void CleanAudiobookTitle_PreservesNovelInMiddleOfTitle()
    {
        // "A Novel Approach to Chess" contains "A Novel" but NOT at the end —
        // it must not be stripped.
        Assert.Equal("A Novel Approach to Chess", Clean("A Novel Approach to Chess"));
    }

    [Fact]
    public void CleanAudiobookTitle_PreservesUnabridgedAtStart()
    {
        // "Unabridged" in the title itself (not in parentheses) must be preserved.
        Assert.Equal("The Unabridged Story", Clean("The Unabridged Story"));
    }

    [Fact]
    public void CleanAudiobookTitle_ReturnsEmptyStringForEmptyInput()
    {
        Assert.Equal("", Clean(""));
    }
}
