using MediaEngine.Api.Services.Playback;
using MediaEngine.Contracts.Playback;

namespace MediaEngine.Api.Tests;

public sealed class AudiobookChapterNormalizerTests
{
    [Fact]
    public void Normalize_ClassifiesShortNumericFirstChapterAsIntroAndRenumbersContent()
    {
        var chapters = AudiobookChapterNormalizer.Normalize(
            [
                Chapter(0, "001", 0, 15),
                Chapter(1, "002", 15, 120),
                Chapter(2, "003", 120, 240),
            ],
            new ListeningSettingsDto());

        Assert.Equal("Intro", chapters[0].Title);
        Assert.Equal(PlaybackChapterKinds.Intro, chapters[0].Kind);
        Assert.Equal(PlaybackChapterTitleSources.Generated, chapters[0].TitleSource);
        Assert.Equal("001", chapters[0].OriginalTitle);
        Assert.Equal("Chapter 1", chapters[1].Title);
        Assert.Equal("Chapter 2", chapters[2].Title);
    }

    [Fact]
    public void Normalize_PreservesMeaningfulShortFirstTitleButMarksItAsIntro()
    {
        var chapters = AudiobookChapterNormalizer.Normalize(
            [
                Chapter(0, "Opening Credits", 0, 12),
                Chapter(1, "1", 12, 180),
            ],
            new ListeningSettingsDto());

        Assert.Equal("Opening Credits", chapters[0].Title);
        Assert.Equal("Opening Credits", chapters[0].OriginalTitle);
        Assert.Equal(PlaybackChapterKinds.Intro, chapters[0].Kind);
        Assert.Equal(PlaybackChapterTitleSources.Embedded, chapters[0].TitleSource);
        Assert.Equal("Chapter 1", chapters[1].Title);
    }

    [Fact]
    public void Normalize_DetectionDisabledLeavesFirstGenericChapterNumbered()
    {
        var chapters = AudiobookChapterNormalizer.Normalize(
            [
                Chapter(0, "001", 0, 15),
                Chapter(1, "002", 15, 120),
            ],
            new ListeningSettingsDto { DetectShortIntroChapters = false });

        Assert.Equal("Chapter 1", chapters[0].Title);
        Assert.Equal(PlaybackChapterKinds.Chapter, chapters[0].Kind);
        Assert.Equal("Chapter 2", chapters[1].Title);
    }

    [Fact]
    public void Normalize_ThresholdControlsIntroClassification()
    {
        var chapters = AudiobookChapterNormalizer.Normalize(
            [
                Chapter(0, "001", 0, 45),
                Chapter(1, "002", 45, 180),
            ],
            new ListeningSettingsDto { ShortIntroMaxSeconds = 60 });

        Assert.Equal("Intro", chapters[0].Title);
        Assert.Equal(PlaybackChapterKinds.Intro, chapters[0].Kind);

        chapters = AudiobookChapterNormalizer.Normalize(
            [
                Chapter(0, "001", 0, 45),
                Chapter(1, "002", 45, 180),
            ],
            new ListeningSettingsDto { ShortIntroMaxSeconds = 30 });

        Assert.Equal("Chapter 1", chapters[0].Title);
        Assert.Equal(PlaybackChapterKinds.Chapter, chapters[0].Kind);
    }

    [Fact]
    public void Normalize_AppliesDisplayTitleOverrideWithoutChangingTimings()
    {
        var chapters = AudiobookChapterNormalizer.Normalize(
            [
                Chapter(0, "001", 0, 15),
                Chapter(1, "002", 15, 120),
            ],
            new ListeningSettingsDto(),
            new Dictionary<int, AudiobookChapterTitleOverrideDto>
            {
                [1] = new()
                {
                    ChapterIndex = 1,
                    Title = "The Crawl Begins",
                    TitleSource = PlaybackChapterTitleSources.AiSuggested,
                },
            });

        Assert.Equal("Intro", chapters[0].Title);
        Assert.Equal("The Crawl Begins", chapters[1].Title);
        Assert.Equal(PlaybackChapterTitleSources.AiSuggested, chapters[1].TitleSource);
        Assert.Equal(15, chapters[1].StartSeconds);
        Assert.Equal(120, chapters[1].EndSeconds);
    }

    [Fact]
    public void ShouldExposeChapterDetails_HidesSingleLargeChapterByDefault()
    {
        var chapters = AudiobookChapterNormalizer.Normalize(
            [Chapter(0, "001", 0, 3600)],
            new ListeningSettingsDto());

        Assert.False(AudiobookChapterNormalizer.ShouldExposeChapterDetails(chapters, new ListeningSettingsDto()));
    }

    [Fact]
    public void ShouldExposeChapterDetails_RespectsConfiguration()
    {
        var chapters = AudiobookChapterNormalizer.Normalize(
            [Chapter(0, "001", 0, 3600)],
            new ListeningSettingsDto());

        Assert.True(AudiobookChapterNormalizer.ShouldExposeChapterDetails(
            chapters,
            new ListeningSettingsDto { HideSingleLargeChapterDetails = false }));
        Assert.True(AudiobookChapterNormalizer.ShouldExposeChapterDetails(
            chapters,
            new ListeningSettingsDto { SingleLargeChapterMinSeconds = 7200 }));
    }

    private static PlaybackChapterDto Chapter(int index, string title, double start, double end) => new()
    {
        Index = index,
        Title = title,
        StartSeconds = start,
        EndSeconds = end,
    };
}
