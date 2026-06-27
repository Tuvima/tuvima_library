using System.Text;
using Dapper;
using MediaEngine.AI.Llama;
using MediaEngine.Contracts.Playback;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.Playback;

public sealed class AudiobookChapterNamingService
{
    private const int MaxPromptChapters = 80;

    private const string SuggestionGrammar = """
        root ::= "{" ws "\"suggestions\"" ws ":" ws "[" ws suggestion-list? ws "]" ws "}"
        suggestion-list ::= suggestion (ws "," ws suggestion)*
        suggestion ::= "{" ws "\"chapterIndex\"" ws ":" ws number ws "," ws "\"suggestedTitle\"" ws ":" ws string ws "," ws "\"confidence\"" ws ":" ws decimal ws "," ws "\"reason\"" ws ":" ws string ws "}"
        string ::= "\"" ([^"\\] | "\\" .)* "\""
        number ::= [0-9]+
        decimal ::= [0-9]+ ("." [0-9]+)?
        ws ::= [ \t\n]*
        """;

    private readonly IDatabaseConnection _db;
    private readonly PlaybackCapabilitiesService _playback;
    private readonly AudiobookChapterTitleOverrideRepository _overrides;
    private readonly ILlamaInferenceService _llama;
    private readonly IEpubContentService _epub;
    private readonly ILogger<AudiobookChapterNamingService> _logger;

    public AudiobookChapterNamingService(
        IDatabaseConnection db,
        PlaybackCapabilitiesService playback,
        AudiobookChapterTitleOverrideRepository overrides,
        ILlamaInferenceService llama,
        IEpubContentService epub,
        ILogger<AudiobookChapterNamingService> logger)
    {
        _db = db;
        _playback = playback;
        _overrides = overrides;
        _llama = llama;
        _epub = epub;
        _logger = logger;
    }

    public Task<IReadOnlyList<AudiobookChapterTitleOverrideDto>> GetOverridesAsync(
        Guid workId,
        Guid? assetId = null,
        CancellationToken ct = default) =>
        _overrides.GetByWorkAsync(workId, assetId, ct);

    public async Task<AudiobookChapterTitleOverrideDto> UpsertOverrideAsync(
        Guid workId,
        UpsertAudiobookChapterTitleOverrideRequestDto request,
        CancellationToken ct = default)
    {
        var asset = await ResolveAudiobookAssetAsync(workId, request.AssetId, ct);
        if (asset is null)
        {
            throw new KeyNotFoundException($"Audiobook asset '{request.AssetId}' was not found for work '{workId}'.");
        }

        return await _overrides.UpsertAsync(workId, request, ct);
    }

    public Task<bool> DeleteOverrideAsync(Guid workId, Guid assetId, int chapterIndex, CancellationToken ct = default) =>
        _overrides.DeleteAsync(workId, assetId, chapterIndex, ct);

    public async Task<AudiobookChapterNameSuggestionsDto> SuggestNamesAsync(
        Guid workId,
        SuggestAudiobookChapterNamesRequestDto request,
        CancellationToken ct = default)
    {
        var asset = await ResolveAudiobookAssetAsync(workId, request.AssetId, ct);
        if (asset is null)
        {
            throw new KeyNotFoundException($"Audiobook work '{workId}' was not found.");
        }

        var warnings = new List<string>();
        var manifest = await _playback.BuildManifestAsync(asset.AssetId, "web", request.ProfileId, ct);
        var chapters = manifest?.Chapters ?? [];
        if (chapters.Count == 0)
        {
            return new AudiobookChapterNameSuggestionsDto
            {
                WorkId = workId,
                AssetId = asset.AssetId,
                Warnings = ["No timed chapters were found for this audiobook."],
            };
        }

        var tocTitles = await LoadLocalEpubTocTitlesAsync(workId, warnings, ct);
        try
        {
            var prompt = BuildSuggestionPrompt(asset, chapters, tocTitles);
            var response = await _llama.InferJsonAsync<ChapterSuggestionEnvelope>(
                AiModelRole.TextQuality,
                prompt,
                SuggestionGrammar,
                ct);
            var suggestions = NormalizeSuggestions(response, chapters);
            if (suggestions.Count == 0)
            {
                warnings.Add("AI did not return any usable chapter title suggestions.");
            }

            return new AudiobookChapterNameSuggestionsDto
            {
                WorkId = workId,
                AssetId = asset.AssetId,
                Suggestions = suggestions,
                Warnings = warnings,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI chapter title suggestions failed for work {WorkId}", workId);
            warnings.Add("AI chapter title suggestions failed. Existing chapter names were not changed.");
            return new AudiobookChapterNameSuggestionsDto
            {
                WorkId = workId,
                AssetId = asset.AssetId,
                Warnings = warnings,
            };
        }
    }

    private async Task<AudiobookAssetContext?> ResolveAudiobookAssetAsync(Guid workId, Guid? assetId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<AudiobookAssetContext>(
            new CommandDefinition(
                """
                SELECT w.id AS WorkId,
                       ma.id AS AssetId,
                       COALESCE(
                           MAX(CASE WHEN wcv.key = 'title' THEN wcv.value END),
                           MAX(CASE WHEN acv.key = 'title' THEN acv.value END),
                           'Untitled audiobook'
                       ) AS Title,
                       COALESCE(
                           MAX(CASE WHEN wcv.key = 'author' THEN wcv.value END),
                           MAX(CASE WHEN acv.key = 'author' THEN acv.value END)
                       ) AS Author,
                       COALESCE(
                           MAX(CASE WHEN wcv.key = 'narrator' THEN wcv.value END),
                           MAX(CASE WHEN acv.key = 'narrator' THEN acv.value END)
                       ) AS Narrator,
                       COALESCE(
                           MAX(CASE WHEN wcv.key = 'series' THEN wcv.value END),
                           MAX(CASE WHEN acv.key = 'series' THEN acv.value END)
                       ) AS Series
                FROM works w
                INNER JOIN editions e ON e.work_id = w.id
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                LEFT JOIN canonical_values wcv ON wcv.entity_id = w.id
                LEFT JOIN canonical_values acv ON acv.entity_id = ma.id
                WHERE w.id = @workId
                  AND LOWER(w.media_type) IN ('audiobook', 'audiobooks', 'audio')
                  AND (@assetId IS NULL OR ma.id = @assetId)
                GROUP BY w.id, ma.id
                ORDER BY ma.presented_at IS NULL, ma.presented_at DESC, ma.file_path_root
                LIMIT 1;
                """,
                new { workId, assetId },
                cancellationToken: ct));
    }

    private async Task<IReadOnlyList<string>> LoadLocalEpubTocTitlesAsync(Guid workId, List<string> warnings, CancellationToken ct)
    {
        string? epubPath;
        using (var conn = _db.CreateConnection())
        {
            epubPath = await conn.QueryFirstOrDefaultAsync<string?>(
                new CommandDefinition(
                    """
                    SELECT ma.file_path_root
                    FROM editions e
                    INNER JOIN media_assets ma ON ma.edition_id = e.id
                    WHERE e.work_id = @workId
                      AND LOWER(ma.file_path_root) LIKE '%.epub'
                    ORDER BY ma.presented_at IS NULL, ma.presented_at DESC, ma.file_path_root
                    LIMIT 1;
                    """,
                    new { workId },
                    cancellationToken: ct));
        }

        if (string.IsNullOrWhiteSpace(epubPath) || !File.Exists(epubPath))
        {
            return [];
        }

        try
        {
            var toc = await _epub.GetTableOfContentsAsync(epubPath, ct);
            return FlattenToc(toc).Take(MaxPromptChapters).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Could not read local EPUB TOC for audiobook chapter naming work {WorkId}", workId);
            warnings.Add("A local EPUB variant was found, but its table of contents could not be read.");
            return [];
        }
    }

    private static IEnumerable<string> FlattenToc(IEnumerable<EpubTocEntry> toc)
    {
        foreach (var item in toc)
        {
            if (!string.IsNullOrWhiteSpace(item.Title))
            {
                yield return item.Title.Trim();
            }

            foreach (var child in FlattenToc(item.Children))
            {
                yield return child;
            }
        }
    }

    private static string BuildSuggestionPrompt(AudiobookAssetContext asset, IReadOnlyList<PlaybackChapterDto> chapters, IReadOnlyList<string> tocTitles)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You suggest display-only audiobook chapter titles.");
        builder.AppendLine("Rules:");
        builder.AppendLine("- Do not change chapter order, chapterIndex, start time, or end time.");
        builder.AppendLine("- Do not invent plot-specific titles unless they are supported by the provided EPUB table of contents.");
        builder.AppendLine("- If there is no reliable better title, use the current title.");
        builder.AppendLine("- Return only JSON matching the grammar.");
        builder.AppendLine(FormattableString.Invariant($"Title: {asset.Title}"));
        builder.AppendLine(FormattableString.Invariant($"Author: {asset.Author ?? "unknown"}"));
        builder.AppendLine(FormattableString.Invariant($"Narrator: {asset.Narrator ?? "unknown"}"));
        builder.AppendLine(FormattableString.Invariant($"Series: {asset.Series ?? "unknown"}"));
        if (tocTitles.Count > 0)
        {
            builder.AppendLine("Local EPUB table of contents:");
            foreach (var title in tocTitles.Take(MaxPromptChapters))
            {
                builder.AppendLine(FormattableString.Invariant($"- {title}"));
            }
        }

        builder.AppendLine("Audiobook chapters:");
        foreach (var chapter in chapters.Take(MaxPromptChapters))
        {
            var duration = chapter.EndSeconds is > 0 && chapter.EndSeconds.Value > chapter.StartSeconds
                ? chapter.EndSeconds.Value - chapter.StartSeconds
                : 0;
            builder.AppendLine(FormattableString.Invariant($"- chapterIndex={chapter.Index}; currentTitle={chapter.Title}; originalTitle={chapter.OriginalTitle ?? "none"}; kind={chapter.Kind}; durationSeconds={duration:0}"));
        }

        builder.AppendLine("Return shape: {\"suggestions\":[{\"chapterIndex\":0,\"suggestedTitle\":\"Intro\",\"confidence\":0.9,\"reason\":\"short intro\"}]}");
        return builder.ToString();
    }

    private static IReadOnlyList<AudiobookChapterNameSuggestionDto> NormalizeSuggestions(
        ChapterSuggestionEnvelope? response,
        IReadOnlyList<PlaybackChapterDto> chapters)
    {
        if (response?.Suggestions is null || response.Suggestions.Count == 0)
        {
            return [];
        }

        var byIndex = chapters.ToDictionary(chapter => chapter.Index);
        return response.Suggestions
            .Where(item => byIndex.ContainsKey(item.ChapterIndex) && !string.IsNullOrWhiteSpace(item.SuggestedTitle))
            .GroupBy(item => item.ChapterIndex)
            .Select(group => group.First())
            .Select(item =>
            {
                var chapter = byIndex[item.ChapterIndex];
                return new AudiobookChapterNameSuggestionDto
                {
                    ChapterIndex = item.ChapterIndex,
                    CurrentTitle = chapter.Title,
                    OriginalTitle = chapter.OriginalTitle,
                    SuggestedTitle = item.SuggestedTitle.Trim(),
                    Confidence = Math.Clamp(item.Confidence, 0d, 1d),
                    Reason = string.IsNullOrWhiteSpace(item.Reason) ? null : item.Reason.Trim(),
                };
            })
            .OrderBy(item => item.ChapterIndex)
            .ToList();
    }

    private sealed record AudiobookAssetContext
    {
        public Guid WorkId { get; init; }
        public Guid AssetId { get; init; }
        public string Title { get; init; } = string.Empty;
        public string? Author { get; init; }
        public string? Narrator { get; init; }
        public string? Series { get; init; }
    }

    private sealed record ChapterSuggestionEnvelope
    {
        public List<ChapterSuggestionItem> Suggestions { get; init; } = [];
    }

    private sealed record ChapterSuggestionItem
    {
        public int ChapterIndex { get; init; }
        public string SuggestedTitle { get; init; } = string.Empty;
        public double Confidence { get; init; }
        public string? Reason { get; init; }
    }
}
