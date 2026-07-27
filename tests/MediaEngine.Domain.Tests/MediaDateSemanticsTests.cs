using MediaEngine.Domain;
using MediaEngine.Domain.Services;

namespace MediaEngine.Domain.Tests;

public sealed class MediaDateSemanticsTests
{
    public static TheoryData<string, Dictionary<string, string>, string> OriginalWorkDates =>
        new()
        {
            {
                "Books",
                new()
                {
                    ["date"] = "1937-09-21",
                    ["release_year"] = "1938",
                    ["year"] = "2012",
                },
                "1937"
            },
            {
                "Audiobooks",
                new()
                {
                    [MetadataFieldConstants.OriginalReleaseYear] = "1937",
                    [MetadataFieldConstants.EditionReleaseYear] = "2007",
                    ["year"] = "2007",
                },
                "1937"
            },
            {
                "Movies",
                new()
                {
                    ["year"] = "2014",
                    ["release_year"] = "2025",
                },
                "2014"
            },
            {
                "TV",
                new()
                {
                    ["first_air_date"] = "2008-01-20",
                    ["year"] = "2013",
                },
                "2008"
            },
            {
                "Comics",
                new()
                {
                    ["year"] = "1984",
                    ["release_year"] = "1988",
                },
                "1984"
            },
            {
                "Music",
                new()
                {
                    [MetadataFieldConstants.OriginalReleaseDate] = "1971-12-17",
                    ["year"] = "2019",
                },
                "1971"
            },
        };

    [Theory]
    [MemberData(nameof(OriginalWorkDates))]
    public void ResolveOriginalYear_UsesMediaAwareWorkDate(
        string mediaType,
        Dictionary<string, string> values,
        string expected)
    {
        Assert.Equal(
            expected,
            MediaDateSemantics.ResolveOriginalYear(
                mediaType,
                key => values.GetValueOrDefault(key)));
    }

    [Fact]
    public void ResolveEditionYear_KeepsEditionDateSeparate()
    {
        var values = new Dictionary<string, string>
        {
            [MetadataFieldConstants.OriginalReleaseYear] = "1937",
            [MetadataFieldConstants.EditionReleaseYear] = "2007",
        };

        Assert.Equal(
            "2007",
            MediaDateSemantics.ResolveEditionYear(key => values.GetValueOrDefault(key)));
    }

    [Fact]
    public void ResolveExplicitOriginalYear_DoesNotTreatRetailYearAsOriginal()
    {
        var values = new Dictionary<string, string>
        {
            ["release_year"] = "2000",
            ["year"] = "2007",
        };

        Assert.Null(
            MediaDateSemantics.ResolveExplicitOriginalYear(
                "Audiobooks",
                key => values.GetValueOrDefault(key)));
    }
}
