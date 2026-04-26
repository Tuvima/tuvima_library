using MediaEngine.Providers.Services;

namespace MediaEngine.Providers.Tests;

public sealed class TextTrackParserTests
{
    [Fact]
    public void LrcParser_ParsesMultipleTimestampsAndSortsLines()
    {
        const string content = """
            [00:12.50]Second line
            [00:01.00][00:03.25]Repeated line
            [ar:Ignored metadata]
            """;

        var lines = LrcParser.Parse(content);

        Assert.Equal(3, lines.Count);
        Assert.Equal(1.0, lines[0].StartSeconds);
        Assert.Equal("Repeated line", lines[0].Text);
        Assert.Equal(3.25, lines[1].StartSeconds);
        Assert.Equal(12.5, lines[2].StartSeconds);
    }

    [Fact]
    public void LrcParser_GetActiveLine_ReturnsLastLineAtOrBeforePlaybackTime()
    {
        var lines = LrcParser.Parse("""
            [00:01.00]One
            [00:04.00]Two
            [00:08.00]Three
            """);

        Assert.Null(LrcParser.GetActiveLine(lines, 0.5));
        Assert.Equal("One", LrcParser.GetActiveLine(lines, 1.0)?.Text);
        Assert.Equal("Two", LrcParser.GetActiveLine(lines, 7.99)?.Text);
        Assert.Equal("Three", LrcParser.GetActiveLine(lines, 10.0)?.Text);
    }

    [Fact]
    public void LrcParser_Normalize_ProducesStableTimestampFormatting()
    {
        const string content = "[0:01.5]First\r\n[01:02.034]Second";

        var normalized = LrcParser.Normalize(content);

        Assert.Equal("[00:01.50] First\n[01:02.03] Second\n", normalized.Replace("\r\n", "\n"));
    }

    [Fact]
    public void SubtitleNormalizer_ConvertsSrtToWebVttAndDecodesHtmlEntities()
    {
        const string srt = """
            1
            00:00:01,250 --> 00:00:03,000
            Hello &amp; welcome

            2
            00:00:05,000 --> 00:00:06,500
            Second line
            """;

        var vtt = SubtitleNormalizer.NormalizeToWebVtt(srt, "srt").Replace("\r\n", "\n");

        Assert.StartsWith("WEBVTT\n\n", vtt);
        Assert.Contains("00:00:01.250 --> 00:00:03.000\nHello & welcome", vtt);
        Assert.Contains("00:00:05.000 --> 00:00:06.500\nSecond line", vtt);
    }

    [Fact]
    public void SubtitleNormalizer_SkipsMalformedSrtTimingBlocks()
    {
        const string srt = """
            1
            not a timing line
            This should be skipped

            2
            00:00:02,000 --> 00:00:03,000
            This should remain
            """;

        var vtt = SubtitleNormalizer.NormalizeToWebVtt(srt, "srt");

        Assert.DoesNotContain("This should be skipped", vtt);
        Assert.Contains("This should remain", vtt);
    }

    [Fact]
    public void SubtitleNormalizer_ConvertsAssDialogueToWebVtt()
    {
        const string ass = """
            [Events]
            Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
            Dialogue: 0,0:00:01.25,0:00:03.00,Default,,0,0,0,,{\i1}Hello\Nthere
            """;

        var vtt = SubtitleNormalizer.NormalizeToWebVtt(ass, "ass").Replace("\r\n", "\n");

        Assert.Contains("00:00:01.250 --> 00:00:03.000\nHello\nthere", vtt);
    }
}
