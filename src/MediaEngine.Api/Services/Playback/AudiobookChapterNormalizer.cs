using System.Text.RegularExpressions;
using MediaEngine.Contracts.Playback;

namespace MediaEngine.Api.Services.Playback;

public static partial class AudiobookChapterNormalizer
{
    public static IReadOnlyList<PlaybackChapterDto> Normalize(
        IReadOnlyList<PlaybackChapterDto> chapters,
        ListeningSettingsDto settings,
        IReadOnlyDictionary<int, AudiobookChapterTitleOverrideDto>? overrides = null)
    {
        if (chapters.Count == 0)
        {
            return [];
        }

        var ordered = chapters
            .Where(chapter => chapter.StartSeconds >= 0)
            .OrderBy(chapter => chapter.StartSeconds)
            .ThenBy(chapter => chapter.Index)
            .ToList();
        if (ordered.Count == 0)
        {
            return [];
        }

        settings ??= new ListeningSettingsDto();
        var introIndex = ResolveIntroIndex(ordered, settings);
        var regularChapterNumber = 1;
        var normalized = new List<PlaybackChapterDto>(ordered.Count);

        foreach (var chapter in ordered)
        {
            var originalTitle = string.IsNullOrWhiteSpace(chapter.OriginalTitle)
                ? chapter.Title
                : chapter.OriginalTitle;
            var isIntro = introIndex == chapter.Index;

            if (overrides is not null
                && overrides.TryGetValue(chapter.Index, out var titleOverride)
                && !string.IsNullOrWhiteSpace(titleOverride.Title))
            {
                normalized.Add(chapter with
                {
                    OriginalTitle = BlankToNull(originalTitle),
                    Kind = isIntro ? PlaybackChapterKinds.Intro : PlaybackChapterKinds.Chapter,
                    Title = titleOverride.Title.Trim(),
                    TitleSource = NormalizeTitleSource(titleOverride.TitleSource),
                });
                if (!isIntro)
                {
                    regularChapterNumber++;
                }

                continue;
            }

            var cleanedOriginal = originalTitle?.Trim();
            var hasMeaningfulTitle = !IsGenericChapterTitle(cleanedOriginal);
            if (isIntro)
            {
                normalized.Add(chapter with
                {
                    OriginalTitle = BlankToNull(originalTitle),
                    Kind = PlaybackChapterKinds.Intro,
                    Title = hasMeaningfulTitle ? cleanedOriginal! : IntroLabel(settings),
                    TitleSource = hasMeaningfulTitle ? PlaybackChapterTitleSources.Embedded : PlaybackChapterTitleSources.Generated,
                });
                continue;
            }

            normalized.Add(chapter with
            {
                OriginalTitle = BlankToNull(originalTitle),
                Kind = PlaybackChapterKinds.Chapter,
                Title = hasMeaningfulTitle ? cleanedOriginal! : $"Chapter {regularChapterNumber}",
                TitleSource = hasMeaningfulTitle ? PlaybackChapterTitleSources.Embedded : PlaybackChapterTitleSources.Generated,
            });
            regularChapterNumber++;
        }

        return normalized;
    }

    public static bool ShouldExposeChapterDetails(
        IReadOnlyList<PlaybackChapterDto> chapters,
        ListeningSettingsDto settings)
    {
        if (chapters.Count == 0)
        {
            return false;
        }

        settings ??= new ListeningSettingsDto();
        if (chapters.Count >= Math.Max(1, settings.MinimumChaptersForChapterDetails))
        {
            return true;
        }

        if (!settings.HideSingleLargeChapterDetails || chapters.Count != 1)
        {
            return true;
        }

        var durationSeconds = ChapterDuration(chapters[0]);
        return !durationSeconds.HasValue || durationSeconds.Value < settings.SingleLargeChapterMinSeconds;
    }

    private static int? ResolveIntroIndex(IReadOnlyList<PlaybackChapterDto> ordered, ListeningSettingsDto settings)
    {
        if (!settings.DetectShortIntroChapters || ordered.Count < 2)
        {
            return null;
        }

        var first = ordered[0];
        if (first.Index != 0 || first.StartSeconds > 5d)
        {
            return null;
        }

        var durationSeconds = ChapterDuration(first);
        return durationSeconds.HasValue && durationSeconds.Value <= settings.ShortIntroMaxSeconds
            ? first.Index
            : null;
    }

    private static double? ChapterDuration(PlaybackChapterDto chapter) =>
        chapter.EndSeconds is > 0 && chapter.EndSeconds.Value > chapter.StartSeconds
            ? chapter.EndSeconds.Value - chapter.StartSeconds
            : null;

    private static string IntroLabel(ListeningSettingsDto settings) =>
        string.IsNullOrWhiteSpace(settings.ShortIntroLabel) ? "Intro" : settings.ShortIntroLabel.Trim();

    private static bool IsGenericChapterTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return true;
        }

        var trimmed = title.Trim();
        return trimmed.All(char.IsDigit)
            || GenericChapterTitleRegex().IsMatch(trimmed);
    }

    private static string NormalizeTitleSource(string? source) =>
        string.Equals(source, PlaybackChapterTitleSources.AiSuggested, StringComparison.OrdinalIgnoreCase)
            ? PlaybackChapterTitleSources.AiSuggested
            : PlaybackChapterTitleSources.Override;

    private static string? BlankToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(@"^(chapter|chap)\s*(\d+)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GenericChapterTitleRegex();
}
