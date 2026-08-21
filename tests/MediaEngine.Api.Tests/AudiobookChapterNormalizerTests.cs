using MediaEngine.Api.Services.Playback;
using MediaEngine.Contracts.Playback;

namespace MediaEngine.Api.Tests;

public sealed class AudiobookChapterNormalizerTests
{
    [Fact]
    public void Normalize_PreservesEmbeddedNumericTrackTitlesAndNeverInfersAnIntro()
    {
        var tracks = AudiobookChapterNormalizer.Normalize(
        [
            Track(0, "001", 0, 15),
            Track(1, "002", 15, 120),
        ]);

        Assert.Equal("001", tracks[0].Title);
        Assert.Equal("002", tracks[1].Title);
        Assert.All(tracks, track => Assert.Equal(PlaybackChapterKinds.Chapter, track.Kind));
        Assert.All(tracks, track => Assert.Equal(PlaybackChapterTitleSources.Embedded, track.TitleSource));
    }

    [Fact]
    public void Normalize_UsesNeutralTrackFallbackOnlyWhenTheEmbeddedTitleIsBlank()
    {
        var tracks = AudiobookChapterNormalizer.Normalize([Track(0, "", 0, 3600)]);

        Assert.Equal("Track 1", tracks[0].Title);
        Assert.Equal(PlaybackChapterTitleSources.Generated, tracks[0].TitleSource);
    }

    [Fact]
    public void Normalize_AppliesAnExplicitOverrideWithoutChangingTrackTiming()
    {
        var tracks = AudiobookChapterNormalizer.Normalize(
            [Track(0, "001", 15, 120)],
            new Dictionary<int, AudiobookChapterTitleOverrideDto>
            {
                [0] = new()
                {
                    ChapterIndex = 0,
                    Title = "The Crawl Begins",
                    TitleSource = PlaybackChapterTitleSources.Override,
                },
            });

        Assert.Equal("The Crawl Begins", tracks[0].Title);
        Assert.Equal(PlaybackChapterTitleSources.Override, tracks[0].TitleSource);
        Assert.Equal(15, tracks[0].StartSeconds);
        Assert.Equal(120, tracks[0].EndSeconds);
    }

    private static PlaybackChapterDto Track(int index, string title, double start, double end) => new()
    {
        Index = index,
        Title = title,
        StartSeconds = start,
        EndSeconds = end,
    };
}
