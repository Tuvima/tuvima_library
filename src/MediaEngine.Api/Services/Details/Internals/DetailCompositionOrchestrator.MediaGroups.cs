using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using MediaEngine.Api.Endpoints;
using MediaEngine.Api.Models;
using MediaEngine.Api.Services.Display;
using MediaEngine.Api.Services.Playback;
using MediaEngine.Api.Services.ReadServices;
using MediaEngine.Contracts.Collections;
using SeriesManifestViewDto = MediaEngine.Domain.Models.SeriesManifestViewDto;
using SeriesManifestItemDto = MediaEngine.Domain.Models.SeriesManifestItemDto;
using MediaEngine.Contracts.Details;
using MediaEngine.Contracts.Persons;
using MediaEngine.Domain;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
using MediaEngine.Storage;
using MediaEngine.Storage.Contracts;
using static MediaEngine.Api.Services.Details.Internals.DetailPresentationPolicy;

namespace MediaEngine.Api.Services.Details.Internals;

internal sealed partial class DetailCompositionOrchestrator
{
    private async Task<IReadOnlyList<MediaGroupingViewModel>> BuildWorkMediaGroupsAsync(Guid workId, DetailEntityType entityType, Guid? profileId, CancellationToken ct)
    {
        if (entityType == DetailEntityType.Audiobook)
        {
            var groups = new List<MediaGroupingViewModel>();
            var chapterGroup = await BuildAudiobookChapterGroupAsync(workId, profileId, ct);
            if (chapterGroup is not null)
            {
                groups.Add(chapterGroup);
            }

            var audioRecommendations = await _recommendations.LoadAsync(workId, entityType, ct);
            if (audioRecommendations.Count > 0)
            {
                groups.Add(new MediaGroupingViewModel
                {
                    Key = "more-like-this",
                    Title = "More Like This",
                    Items = audioRecommendations,
                });
            }

            return groups;
        }

        var recommendations = await _recommendations.LoadAsync(workId, entityType, ct);
        if (recommendations.Count == 0)
        {
            return [];
        }

        return
        [
            new MediaGroupingViewModel
            {
                Key = "more-like-this",
                Title = "More Like This",
                Items = recommendations,
            }
        ];
    }

    private async Task<MediaGroupingViewModel?> BuildAudiobookChapterGroupAsync(Guid workId, Guid? profileId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync<AudiobookAssetRow>(new CommandDefinition(
            """
            SELECT w.id AS WorkId,
                   ma.id AS AssetId,
                   COALESCE(
                       MAX(CASE WHEN wcv.key = 'title' THEN wcv.value END),
                       MAX(CASE WHEN acv.key = 'title' THEN acv.value END),
                       'Full audiobook'
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
                       MAX(CASE WHEN wcv.key IN ('duration_seconds', 'duration_sec') THEN wcv.value END),
                       MAX(CASE WHEN acv.key IN ('duration_seconds', 'duration_sec') THEN acv.value END)
                   ) AS DurationSecondsValue,
                   COALESCE(
                       MAX(CASE WHEN wcv.key = 'duration' THEN wcv.value END),
                       MAX(CASE WHEN acv.key = 'duration' THEN acv.value END),
                       MAX(CASE WHEN wcv.key = 'runtime' THEN wcv.value END),
                       MAX(CASE WHEN acv.key = 'runtime' THEN acv.value END)
                   ) AS Duration
            FROM works w
            INNER JOIN editions e ON e.work_id = w.id
            INNER JOIN media_assets ma ON ma.edition_id = e.id
            LEFT JOIN canonical_values wcv ON wcv.entity_id = w.id
            LEFT JOIN canonical_values acv ON acv.entity_id = ma.id
            WHERE w.id = @workId
              AND LOWER(w.media_type) IN ('audiobook', 'audiobooks', 'audio')
            GROUP BY w.id, ma.id
            ORDER BY ma.presented_at IS NULL, ma.presented_at DESC, ma.file_path_root
            LIMIT 1;
            """,
            new { workId },
            cancellationToken: ct));

        if (row is null)
        {
            return null;
        }

        MediaEngine.Contracts.Playback.PlaybackManifestDto? manifest = null;
        if (_playback is not null && row.AssetId != Guid.Empty)
        {
            try
            {
                manifest = await _playback.BuildManifestAsync(row.AssetId, "web", profileId, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(
                    ex,
                    "Could not build playback manifest for audiobook asset {AssetId}; falling back to full-audiobook detail row.",
                    row.AssetId);
            }
        }

        var chapters = manifest?.Chapters ?? [];
        var totalDurationSeconds = ResolveAudiobookTotalDurationSeconds(row, chapters);
        var resume = await LoadAudiobookResumeAsync(conn, row.WorkId, row.AssetId, manifest?.Resume, totalDurationSeconds, ct);
        var resumeSeconds = resume?.PositionSeconds;
        var items = chapters.Count > 0
            ? chapters.Select(chapter => ToAudiobookChapterItem(row, chapter, resumeSeconds)).ToList()
            : [ToFullAudiobookItem(row, manifest, resume)];

        return new MediaGroupingViewModel
        {
            Key = "chapters",
            Title = chapters.Count > 0 ? "Chapters" : "Playback",
            Items = items,
            OwnedCount = items.Count,
            TotalCount = items.Count,
        };
    }

    private static MediaGroupingItemViewModel ToAudiobookChapterItem(AudiobookAssetRow row, MediaEngine.Contracts.Playback.PlaybackChapterDto chapter, double? resumeSeconds)
    {
        var durationSeconds = chapter.EndSeconds.HasValue && chapter.EndSeconds.Value > chapter.StartSeconds
            ? chapter.EndSeconds.Value - chapter.StartSeconds
            : chapter.Index == 0 && chapter.StartSeconds <= 0
                ? TryParseAudioDurationSeconds(row.DurationSecondsValue) ?? TryParseDurationSeconds(row.Duration)
                : (double?)null;
        var progressPercent = CalculateChapterProgress(resumeSeconds, chapter.StartSeconds, chapter.EndSeconds);

        return new MediaGroupingItemViewModel
        {
            Id = row.WorkId.ToString("D"),
            EntityType = DetailEntityType.Audiobook,
            Title = string.IsNullOrWhiteSpace(chapter.Title) ? $"Chapter {chapter.Index + 1}" : chapter.Title,
            Subtitle = FirstText(row.Author, row.Narrator),
            ArtworkUrl = $"/stream/{row.AssetId}/cover",
            TrackNumber = (chapter.Index + 1).ToString(CultureInfo.InvariantCulture),
            Duration = FormatSecondsDuration(durationSeconds),
            DurationSeconds = durationSeconds,
            AssetId = row.AssetId.ToString("D"),
            ChapterIndex = chapter.Index,
            StartSeconds = chapter.StartSeconds,
            EndSeconds = chapter.EndSeconds,
            ResumePositionSeconds = IsPositionWithinChapter(resumeSeconds, chapter.StartSeconds, chapter.EndSeconds) ? resumeSeconds : null,
            ProgressPercent = progressPercent,
            Metadata = BuildEpisodeMetadata(FormatSecondsDuration(durationSeconds), null),
            Actions = [new DetailAction { Key = "play-chapter", Label = progressPercent is > 0 and < 100 ? "Continue" : "Play", Icon = "play_arrow" }],
            IsOwned = true,
            ProgressState = progressPercent >= 100
                ? LibraryProgressState.Completed
                : progressPercent is > 0
                    ? LibraryProgressState.InProgress
                    : LibraryProgressState.Unstarted,
        };
    }

    private static MediaGroupingItemViewModel ToFullAudiobookItem(AudiobookAssetRow row, MediaEngine.Contracts.Playback.PlaybackManifestDto? manifest, MediaEngine.Contracts.Playback.PlaybackResumeDto? resume)
    {
        double? durationSeconds = TryParseAudioDurationSeconds(row.DurationSecondsValue)
            ?? TryParseDurationSeconds(row.Duration);
        durationSeconds ??= manifest?.Chapters
            .Where(chapter => chapter.EndSeconds.HasValue)
            .Select(chapter => chapter.EndSeconds!.Value)
            .DefaultIfEmpty()
            .Max();
        if (durationSeconds <= 0)
        {
            durationSeconds = null;
        }

        return new MediaGroupingItemViewModel
        {
            Id = row.WorkId.ToString("D"),
            EntityType = DetailEntityType.Audiobook,
            Title = "Full audiobook",
            Subtitle = FirstText(row.Author, row.Narrator),
            ArtworkUrl = $"/stream/{row.AssetId}/cover",
            TrackNumber = "1",
            Duration = FormatSecondsDuration(durationSeconds),
            DurationSeconds = durationSeconds,
            AssetId = row.AssetId.ToString("D"),
            ChapterIndex = 0,
            StartSeconds = 0,
            EndSeconds = durationSeconds,
            ResumePositionSeconds = resume?.PositionSeconds,
            ProgressPercent = resume?.ProgressPct,
            Metadata = BuildEpisodeMetadata(FormatSecondsDuration(durationSeconds), null),
            Actions = [new DetailAction { Key = "play-chapter", Label = resume?.PositionSeconds is > 0 ? "Continue" : "Play", Icon = "play_arrow" }],
            IsOwned = true,
            ProgressState = resume?.ProgressPct >= 100
                ? LibraryProgressState.Completed
                : resume?.PositionSeconds is > 0
                    ? LibraryProgressState.InProgress
                    : LibraryProgressState.Unstarted,
        };
    }

    private static async Task<MediaEngine.Contracts.Playback.PlaybackResumeDto?> LoadAudiobookResumeAsync(
        System.Data.IDbConnection conn,
        Guid workId,
        Guid assetId,
        MediaEngine.Contracts.Playback.PlaybackResumeDto? fallback,
        double? durationSeconds,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var rows = (await conn.QueryAsync<AudiobookResumeRow>(new CommandDefinition(
            """
            SELECT 0 AS SourceRank,
                   last_position_seconds AS PositionSeconds,
                   duration_seconds AS DurationSeconds,
                   CASE WHEN duration_seconds IS NOT NULL AND duration_seconds > 0 THEN (last_position_seconds / duration_seconds) * 100.0 ELSE NULL END AS ProgressPct,
                   last_heartbeat_at AS LastAccessed,
                   NULL AS ExtendedProperties
            FROM audiobook_listen_active_segments
            WHERE work_id = @workId
              AND asset_id = @assetId
            UNION ALL
            SELECT 1 AS SourceRank,
                   position_seconds AS PositionSeconds,
                   duration_seconds AS DurationSeconds,
                   progress_pct AS ProgressPct,
                   ended_at AS LastAccessed,
                   NULL AS ExtendedProperties
            FROM audiobook_listen_history
            WHERE work_id = @workId
              AND asset_id = @assetId
            UNION ALL
            SELECT 2 AS SourceRank,
                   NULL AS PositionSeconds,
                   NULL AS DurationSeconds,
                   progress_pct AS ProgressPct,
                   last_accessed AS LastAccessed,
                   extended_properties AS ExtendedProperties
            FROM user_states
            WHERE asset_id = @assetId
            ORDER BY SourceRank ASC, LastAccessed DESC
            LIMIT 25;
            """,
            new { workId, assetId },
            cancellationToken: ct))).ToList();

        if (rows.Count == 0)
        {
            return NormalizeAudiobookResumePosition(fallback, durationSeconds);
        }

        var resumes = rows
            .Select(row => BuildAudiobookResume(row, fallback, durationSeconds))
            .Where(resume => resume is not null)
            .Select(resume => resume!)
            .ToList();

        return resumes.FirstOrDefault(IsMeaningfulAudiobookResume)
            ?? (IsMeaningfulAudiobookResume(NormalizeAudiobookResumePosition(fallback, durationSeconds))
                ? NormalizeAudiobookResumePosition(fallback, durationSeconds)
                : null)
            ?? resumes.FirstOrDefault()
            ?? NormalizeAudiobookResumePosition(fallback, durationSeconds);
    }

    private static MediaEngine.Contracts.Playback.PlaybackResumeDto? BuildAudiobookResume(
        AudiobookResumeRow row,
        MediaEngine.Contracts.Playback.PlaybackResumeDto? fallback,
        double? knownDurationSeconds)
    {
        var positionSeconds = row.PositionSeconds
            ?? TryReadExtendedPropertyDouble(row.ExtendedProperties, "position_seconds")
            ?? (row.SourceRank == 2 ? fallback?.PositionSeconds : null);
        var durationSeconds = row.DurationSeconds
            ?? TryReadExtendedPropertyDouble(row.ExtendedProperties, "duration_seconds")
            ?? knownDurationSeconds;
        var progressPct = row.ProgressPct
            ?? (positionSeconds.HasValue && durationSeconds is > 0
                ? positionSeconds.Value / durationSeconds.Value * 100d
                : fallback?.ProgressPct);
        if (!positionSeconds.HasValue && progressPct is > 0 and < 100 && durationSeconds is > 0)
        {
            positionSeconds = durationSeconds.Value * Math.Clamp(progressPct.Value, 0, 100) / 100d;
        }
        if (positionSeconds is >= 0
            && progressPct is > 1 and < 100
            && durationSeconds is > 0
            && positionSeconds.Value <= new MediaEngine.Contracts.Playback.ListeningSettingsDto().AudiobookNearStartGuardSeconds)
        {
            positionSeconds = durationSeconds.Value * Math.Clamp(progressPct.Value, 0, 100) / 100d;
        }
        if (positionSeconds.HasValue)
        {
            positionSeconds = durationSeconds is > 0
                ? Math.Clamp(positionSeconds.Value, 0, durationSeconds.Value)
                : Math.Max(0, positionSeconds.Value);
        }

        if (!positionSeconds.HasValue && !progressPct.HasValue)
        {
            return NormalizeAudiobookResumePosition(fallback, knownDurationSeconds);
        }

        return new MediaEngine.Contracts.Playback.PlaybackResumeDto
        {
            PositionSeconds = positionSeconds,
            ProgressPct = Math.Clamp(progressPct ?? 0, 0, 100),
            LastAccessed = DateTimeOffset.TryParse(row.LastAccessed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed
                : fallback?.LastAccessed,
        };
    }

    private static double? ResolveAudiobookTotalDurationSeconds(
        AudiobookAssetRow row,
        IReadOnlyList<MediaEngine.Contracts.Playback.PlaybackChapterDto> chapters)
    {
        var chapterEnd = chapters
            .Where(chapter => chapter.EndSeconds is > 0)
            .Select(chapter => chapter.EndSeconds!.Value)
            .DefaultIfEmpty()
            .Max();
        if (chapterEnd > 0)
        {
            return chapterEnd;
        }

        return TryParseAudioDurationSeconds(row.DurationSecondsValue)
            ?? TryParseDurationSeconds(row.Duration);
    }

    private static MediaEngine.Contracts.Playback.PlaybackResumeDto? NormalizeAudiobookResumePosition(
        MediaEngine.Contracts.Playback.PlaybackResumeDto? resume,
        double? durationSeconds)
    {
        if (resume is null)
        {
            return null;
        }

        var duration = durationSeconds is > 0 ? durationSeconds.Value : (double?)null;
        var progress = Math.Clamp(resume.ProgressPct, 0, 100);
        var position = resume.PositionSeconds;
        if (!position.HasValue && progress is > 0 and < 100 && duration.HasValue)
        {
            position = duration.Value * progress / 100d;
        }

        if (position.HasValue)
        {
            position = duration.HasValue
                ? Math.Clamp(position.Value, 0, duration.Value)
                : Math.Max(0, position.Value);
            if (duration.HasValue && progress <= 0)
            {
                progress = Math.Clamp(position.Value / duration.Value * 100d, 0, 100);
            }
        }

        return resume with
        {
            PositionSeconds = position,
            ProgressPct = progress,
        };
    }

    private static bool IsMeaningfulAudiobookResume(MediaEngine.Contracts.Playback.PlaybackResumeDto? resume)
        => resume?.PositionSeconds is > 0 || resume?.ProgressPct is > 0;

    private static bool IsPositionWithinChapter(double? positionSeconds, double startSeconds, double? endSeconds)
        => positionSeconds.HasValue
            && positionSeconds.Value >= startSeconds
            && (!endSeconds.HasValue || positionSeconds.Value < endSeconds.Value);

    private static double? TryReadExtendedPropertyDouble(string? extendedProperties, string key)
    {
        if (string.IsNullOrWhiteSpace(extendedProperties))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(extendedProperties);
            if (!doc.RootElement.TryGetProperty(key, out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var numeric))
            {
                return numeric;
            }

            if (value.ValueKind == JsonValueKind.String
                && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string SeriesMediaFilter(DetailEntityType entityType, string mediaType)
         => entityType switch
         {
             DetailEntityType.Book or DetailEntityType.ComicIssue or DetailEntityType.Work when mediaType.Contains("book", StringComparison.OrdinalIgnoreCase)
                 || mediaType.Contains("comic", StringComparison.OrdinalIgnoreCase)
                 || mediaType.Equals("Books", StringComparison.OrdinalIgnoreCase)
                 || mediaType.Equals("Comics", StringComparison.OrdinalIgnoreCase) => "Read",
             DetailEntityType.Audiobook => "Listen",
             DetailEntityType.Movie or DetailEntityType.TvShow or DetailEntityType.TvSeason or DetailEntityType.TvEpisode => "Watch",
             DetailEntityType.MusicAlbum => "Music",
             _ when mediaType.Contains("audio", StringComparison.OrdinalIgnoreCase) => "Listen",
             _ when mediaType.Contains("movie", StringComparison.OrdinalIgnoreCase) || mediaType.Equals("TV", StringComparison.OrdinalIgnoreCase) => "Watch",
             _ => "Other",
         };

}
