using MediaEngine.Api.Services.Details;

namespace MediaEngine.Api.Tests;

public sealed class SeriesDisplayFormatterTests
{
    [Theory]
    [InlineData("Dune Collection", "Dune")]
    [InlineData("The Expanse Series", "The Expanse")]
    [InlineData("The Expanse", "The Expanse")]
    public void NormalizeContainerTitle_RemovesOnlyStructuralSuffixes(string input, string expected)
    {
        Assert.Equal(expected, SeriesDisplayFormatter.NormalizeContainerTitle(input, isStructuralSeries: true));
    }

    [Fact]
    public void NormalizeContainerTitle_PreservesCuratedCollectionNames()
    {
        Assert.Equal(
            "The Criterion Collection",
            SeriesDisplayFormatter.NormalizeContainerTitle("The Criterion Collection", isStructuralSeries: false));
    }

    [Theory]
    [InlineData("Book", "1", "The Expanse Series", "Book 1 in The Expanse")]
    [InlineData("Movie", "1", "The Lord of the Rings Collection", "Movie 1 in The Lord of the Rings")]
    public void FormatPosition_UsesDescriptivePositionWithoutASequenceTotal(
        string itemLabel,
        string position,
        string container,
        string expected)
    {
        Assert.Equal(expected, SeriesDisplayFormatter.FormatPosition(itemLabel, position, container));
    }

    [Fact]
    public void FormatPosition_UsesMembershipCopyWhenThePositionIsUnknown()
    {
        Assert.Equal("Part of The Expanse", SeriesDisplayFormatter.FormatPosition("Book", null, "The Expanse"));
    }

    [Fact]
    public void FormatEpisodePosition_UsesSeasonEpisodeAndShowName()
    {
        Assert.Equal(
            "Season 2, Episode 3 in The Expanse",
            SeriesDisplayFormatter.FormatEpisodePosition("2", "E3", "The Expanse Series"));
    }
}
