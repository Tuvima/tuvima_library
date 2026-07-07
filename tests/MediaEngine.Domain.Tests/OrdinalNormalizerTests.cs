using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Services;

namespace MediaEngine.Domain.Tests;

public sealed class OrdinalNormalizerTests
{
    [Theory]
    [InlineData("1", 1.0, 1)]
    [InlineData("3.5", 3.5, null)]
    [InlineData("#12", 12.0, 12)]
    [InlineData("Annual #1", 10001.0, 10001)]
    public void Normalize_ReturnsStableSortValues(string input, double expectedSort, int? expectedInteger)
    {
        var result = OrdinalNormalizer.Normalize(input);

        Assert.Equal(expectedSort, result.SortValue);
        Assert.Equal(expectedInteger, OrdinalNormalizer.IntegerOrdinal(result.SortValue));
    }

    [Fact]
    public void Normalize_AnnualMarksSequenceFormat()
    {
        var result = OrdinalNormalizer.Normalize("Annual #1");

        Assert.Equal(SequenceFormat.Annual, result.FormatIndicator);
    }

    [Fact]
    public void NormalizeDiscTrack_SeparatesMatchingTrackNumbersAcrossDiscs()
    {
        var discOne = OrdinalNormalizer.NormalizeDiscTrack("1", "1");
        var discTwo = OrdinalNormalizer.NormalizeDiscTrack("2", "1");

        Assert.Equal(1.0, discOne.SortValue);
        Assert.Equal(2001.0, discTwo.SortValue);
        Assert.NotEqual(discOne.SortValue, discTwo.SortValue);
    }
}
