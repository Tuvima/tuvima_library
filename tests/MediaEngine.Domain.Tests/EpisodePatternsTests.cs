using MediaEngine.Domain.Services;

namespace MediaEngine.Domain.Tests;

public sealed class EpisodePatternsTests
{
    [Fact]
    public void SeasonEpisode_MatchesTwoDigitSeasonAndEpisode()
    {
        var match = EpisodePatterns.SeasonEpisode().Match("Show.Name.S02E05");

        Assert.True(match.Success);
        Assert.Equal("Show.Name", match.Groups["series"].Value);
        Assert.Equal("02", match.Groups["season"].Value);
        Assert.Equal("05", match.Groups["ep1"].Value);
        Assert.False(match.Groups["ep2"].Success);
    }

    [Fact]
    public void SeasonEpisode_MatchesOneDigitSeasonAndEpisode()
    {
        // "S1E1" style short-form numbering, with a required series prefix
        // (see the no-series-prefix caveat below).
        var match = EpisodePatterns.SeasonEpisode().Match("MyShow.S1E1");

        Assert.True(match.Success);
        Assert.Equal("MyShow", match.Groups["series"].Value);
        Assert.Equal("1", match.Groups["season"].Value);
        Assert.Equal("1", match.Groups["ep1"].Value);
        Assert.False(match.Groups["ep2"].Success);
    }

    [Fact]
    public void SeasonEpisode_MatchesDoubleEpisodeFormat()
    {
        var match = EpisodePatterns.SeasonEpisode().Match("Show.Name.S01E01E02");

        Assert.True(match.Success);
        Assert.Equal("Show.Name", match.Groups["series"].Value);
        Assert.Equal("01", match.Groups["season"].Value);
        Assert.Equal("01", match.Groups["ep1"].Value);
        Assert.True(match.Groups["ep2"].Success);
        Assert.Equal("02", match.Groups["ep2"].Value);
    }

    [Fact]
    public void SeasonEpisode_IsCaseInsensitiveForSAndEMarkers()
    {
        var match = EpisodePatterns.SeasonEpisode().Match("show.name.s02e05");

        Assert.True(match.Success);
        Assert.Equal("02", match.Groups["season"].Value);
        Assert.Equal("05", match.Groups["ep1"].Value);
    }

    [Fact]
    public void SeasonEpisode_DoesNotMatch_BareEpisodeCodeWithoutSeriesPrefix()
    {
        // The shared pattern requires a non-empty "series" capture (its quantifier
        // is `.+?`, one-or-more). A filename that opens directly with the episode
        // code — no series title before it — does not match this pattern; the
        // original call sites (SmartLabeler, VideoProcessor) handle that case with
        // a separate leading-pattern regex that this packet does not unify.
        var match = EpisodePatterns.SeasonEpisode().Match("S1E1");

        Assert.False(match.Success);
    }
}
