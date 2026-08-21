using MediaEngine.Contracts.Playback;

namespace MediaEngine.Api.Services.Playback;

public static class AudiobookChapterNormalizer
{
    public static IReadOnlyList<PlaybackChapterDto> Normalize(
        IReadOnlyList<PlaybackChapterDto> chapters,
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

        var normalized = new List<PlaybackChapterDto>(ordered.Count);

        foreach (var chapter in ordered)
        {
            var originalTitle = string.IsNullOrWhiteSpace(chapter.OriginalTitle)
                ? chapter.Title
                : chapter.OriginalTitle;
            if (overrides is not null
                && overrides.TryGetValue(chapter.Index, out var titleOverride)
                && !string.IsNullOrWhiteSpace(titleOverride.Title))
            {
                normalized.Add(chapter with
                {
                    OriginalTitle = BlankToNull(originalTitle),
                    Kind = PlaybackChapterKinds.Chapter,
                    Title = titleOverride.Title.Trim(),
                    TitleSource = PlaybackChapterTitleSources.Override,
                });
                continue;
            }

            normalized.Add(chapter with
            {
                OriginalTitle = BlankToNull(originalTitle),
                Kind = PlaybackChapterKinds.Chapter,
                Title = string.IsNullOrWhiteSpace(originalTitle) ? $"Track {chapter.Index + 1}" : originalTitle.Trim(),
                TitleSource = string.IsNullOrWhiteSpace(originalTitle)
                    ? PlaybackChapterTitleSources.Generated
                    : PlaybackChapterTitleSources.Embedded,
            });
        }

        return normalized;
    }

    private static string? BlankToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
