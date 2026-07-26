using MediaEngine.Domain;
using MediaEngine.Domain.Enums;
using MediaEngine.Providers.Services;

namespace MediaEngine.Providers.Tests;

/// <summary>
/// Pins the behavior of <see cref="RetailHints"/>, the shared static consolidating scoring
/// helpers that were previously duplicated across <c>RetailCandidateScorer</c>,
/// <c>RetailMatchScoringService</c>, <c>RetailMatchWorker</c>, <c>SearchService</c>,
/// <c>ReconciliationAdapter</c>, <c>CollectionAssignmentService</c>,
/// <c>PersonEnrichmentWorker</c>, and <c>RecursiveIdentityService</c>.
/// </summary>
public sealed class RetailHintsTests
{
    // ── NormalizeYear ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2019", "2019")]
    [InlineData("2019-03-01", "2019")]
    [InlineData("(2019)", "2019")]
    [InlineData("Released in 2019, remastered", "2019")]
    [InlineData("no year here", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("12345", null)] // 5 digits does not match \b\d{4}\b as a standalone token
    public void NormalizeYear_ExtractsFourDigitToken(string? input, string? expected)
    {
        Assert.Equal(expected, RetailHints.NormalizeYear(input));
    }

    // ── GetYearHint ──────────────────────────────────────────────────────────

    [Fact]
    public void GetYearHint_PrefersYearKey()
    {
        var hints = new Dictionary<string, string>
        {
            [MetadataFieldConstants.Year] = "2020",
            ["release_year"] = "1999",
            ["date"] = "1980-01-01",
            ["release_date"] = "1970",
        };

        Assert.Equal("2020", RetailHints.GetYearHint(hints));
    }

    [Fact]
    public void GetYearHint_FallsBackThroughChainInOrder()
    {
        var releaseYearOnly = new Dictionary<string, string> { ["release_year"] = "1999" };
        Assert.Equal("1999", RetailHints.GetYearHint(releaseYearOnly));

        var dateOnly = new Dictionary<string, string> { ["date"] = "1980-06-15" };
        Assert.Equal("1980", RetailHints.GetYearHint(dateOnly));

        var releaseDateOnly = new Dictionary<string, string> { ["release_date"] = "1970" };
        Assert.Equal("1970", RetailHints.GetYearHint(releaseDateOnly));
    }

    [Fact]
    public void GetYearHint_NoMatchingKeys_ReturnsNull()
    {
        var hints = new Dictionary<string, string> { ["title"] = "Some Title" };
        Assert.Null(RetailHints.GetYearHint(hints));
    }

    // ── GetCreatorHint (flat, media-type-agnostic overload) ────────────────

    [Fact]
    public void GetCreatorHint_Flat_PrefersAuthorOverEverythingElse()
    {
        var hints = new Dictionary<string, string>
        {
            [MetadataFieldConstants.Author] = "Author Name",
            [MetadataFieldConstants.Artist] = "Artist Name",
            [MetadataFieldConstants.Series] = "Series Name",
        };

        Assert.Equal("Author Name", RetailHints.GetCreatorHint(hints));
    }

    [Fact]
    public void GetCreatorHint_Flat_FallsBackToShowNameThenSeries()
    {
        var showNameOnly = new Dictionary<string, string> { [MetadataFieldConstants.ShowName] = "The Show" };
        Assert.Equal("The Show", RetailHints.GetCreatorHint(showNameOnly));

        var seriesOnly = new Dictionary<string, string> { [MetadataFieldConstants.Series] = "The Series" };
        Assert.Equal("The Series", RetailHints.GetCreatorHint(seriesOnly));
    }

    [Fact]
    public void GetCreatorHint_Flat_FallsBackToWriterBeforeShowName()
    {
        var hints = new Dictionary<string, string>
        {
            ["writer"] = "Writer Name",
            [MetadataFieldConstants.ShowName] = "The Show",
        };

        Assert.Equal("Writer Name", RetailHints.GetCreatorHint(hints));
    }

    // ── GetCreatorHint (media-type-aware overload) ─────────────────────────

    [Fact]
    public void GetCreatorHint_MediaTypeAware_Music_PrefersArtistOverAuthor()
    {
        var hints = new Dictionary<string, string>
        {
            [MetadataFieldConstants.Author] = "Author Name",
            [MetadataFieldConstants.Artist] = "Artist Name",
        };

        Assert.Equal("Artist Name", RetailHints.GetCreatorHint(hints, MediaType.Music));
    }

    [Fact]
    public void GetCreatorHint_MediaTypeAware_Tv_PrefersAuthorThenShowNameThenSeries()
    {
        var authorHints = new Dictionary<string, string>
        {
            [MetadataFieldConstants.Author] = "Author Name",
            [MetadataFieldConstants.ShowName] = "The Show",
        };
        Assert.Equal("Author Name", RetailHints.GetCreatorHint(authorHints, MediaType.TV));

        var showNameOnly = new Dictionary<string, string> { [MetadataFieldConstants.ShowName] = "The Show" };
        Assert.Equal("The Show", RetailHints.GetCreatorHint(showNameOnly, MediaType.TV));
    }

    [Fact]
    public void GetCreatorHint_MediaTypeAware_Comics_PrefersAuthorThenWriterThenIllustrator()
    {
        var illustratorOnly = new Dictionary<string, string>
        {
            [MetadataFieldConstants.Illustrator] = "Illustrator Name",
        };

        Assert.Equal("Illustrator Name", RetailHints.GetCreatorHint(illustratorOnly, MediaType.Comics));
    }

    [Fact]
    public void GetCreatorHint_MediaTypeAware_DefaultCase_DoesNotFallBackToShowNameOrSeries()
    {
        // Regression pin: the media-type-aware default case (unlike the flat overload) has no
        // show-name/series fallback. This is the exact behavior difference documented in
        // RetailHints — it must not silently gain the extra fallback.
        var hints = new Dictionary<string, string> { [MetadataFieldConstants.ShowName] = "The Show" };

        Assert.Null(RetailHints.GetCreatorHint(hints, MediaType.Books));
    }

    // ── AreEquivalentOrdinals / ExtractLeadingDigits ────────────────────────

    [Theory]
    [InlineData("3", "03", true)]
    [InlineData("issue 3", "Issue No. 3", true)]
    [InlineData("007", "7", true)]
    [InlineData("1", "2", false)]
    [InlineData("annual", "annual", true)]
    [InlineData("annual", "special", false)]
    public void AreEquivalentOrdinals_ComparesNumericallyThenTextually(string left, string right, bool expected)
    {
        Assert.Equal(expected, RetailHints.AreEquivalentOrdinals(left, right));
    }

    [Theory]
    [InlineData("No. 007", "7")]
    [InlineData("Issue 42", "42")]
    [InlineData("003", "3")]
    [InlineData("annual", "")]
    [InlineData("", "")]
    public void ExtractLeadingDigits_StripsLeadingNonDigitsAndZeros(string input, string expected)
    {
        Assert.Equal(expected, RetailHints.ExtractLeadingDigits(input));
    }

    // ── SplitAuthors ─────────────────────────────────────────────────────────

    [Fact]
    public void SplitAuthors_SplitsOnAmpersandAndOrComma()
    {
        Assert.Equal(
            ["Neil Gaiman", "Terry Pratchett"],
            RetailHints.SplitAuthors("Neil Gaiman & Terry Pratchett"));

        Assert.Equal(
            ["Neil Gaiman", "Terry Pratchett"],
            RetailHints.SplitAuthors("Neil Gaiman and Terry Pratchett"));

        Assert.Equal(
            ["Neil Gaiman", "Terry Pratchett"],
            RetailHints.SplitAuthors("Neil Gaiman, Terry Pratchett"));
    }

    [Fact]
    public void SplitAuthors_SingleAuthor_ReturnsSingleElementList()
    {
        Assert.Equal(["J. R. R. Tolkien"], RetailHints.SplitAuthors("J. R. R. Tolkien"));
    }

    [Fact]
    public void SplitAuthors_TrimsAndDropsEmptyParts()
    {
        Assert.Equal(["A", "B"], RetailHints.SplitAuthors("A ,  , B"));
    }

    // ── NormalizeQid ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Q42", "Q42")]
    [InlineData("http://www.wikidata.org/entity/Q42", "Q42")]
    [InlineData("Q42::Douglas Adams", "Q42")]
    [InlineData("http://www.wikidata.org/entity/Q42::Douglas Adams", "Q42")]
    [InlineData("  Q42  ", "Q42")]
    public void NormalizeQid_StripsUriPrefixAndLabelSuffix(string input, string expected)
    {
        Assert.Equal(expected, RetailHints.NormalizeQid(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-qid")]
    [InlineData("42")]
    public void NormalizeQid_InvalidOrMissing_ReturnsNull(string? input)
    {
        Assert.Null(RetailHints.NormalizeQid(input));
    }

    // ── NormalizePersonNameKey ───────────────────────────────────────────────

    [Theory]
    [InlineData("John   Smith", "JOHN SMITH")]
    [InlineData("  john smith  ", "JOHN SMITH")]
    [InlineData("Jane", "JANE")]
    public void NormalizePersonNameKey_CollapsesWhitespaceAndUppercases(string input, string expected)
    {
        Assert.Equal(expected, RetailHints.NormalizePersonNameKey(input));
    }

    // ── NormalizeBibliographicPersonName ────────────────────────────────────

    [Theory]
    [InlineData("Adams, Douglas", "Douglas Adams")]
    [InlineData("Tolkien,J.R.R.", "J.R.R. Tolkien")]
    [InlineData("Douglas Adams", "Douglas Adams")] // no comma: unchanged
    [InlineData("Adams, Douglas, Jr.", "Adams, Douglas, Jr.")] // multiple commas: unchanged
    [InlineData("Adams,", "Adams,")] // empty first name after comma: unchanged
    public void NormalizeBibliographicPersonName_ReversesLastCommaFirst(string input, string expected)
    {
        Assert.Equal(expected, RetailHints.NormalizeBibliographicPersonName(input));
    }
}
