using System.Globalization;
using MediaEngine.Domain;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Services;
using MediaEngine.Providers.Services;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Workers;

public sealed class TextTrackEnrichmentWorker
{
    private readonly IMediaAssetRepository _assetRepo;
    private readonly IWorkRepository _workRepo;
    private readonly ICanonicalValueRepository _canonicalRepo;
    private readonly IBridgeIdRepository _bridgeRepo;
    private readonly ITextTrackRepository _trackRepo;
    private readonly IEnumerable<ITextTrackProvider> _providers;
    private readonly AssetPathService _assetPaths;
    private readonly ILogger<TextTrackEnrichmentWorker> _logger;

    public TextTrackEnrichmentWorker(
        IMediaAssetRepository assetRepo,
        IWorkRepository workRepo,
        ICanonicalValueRepository canonicalRepo,
        IBridgeIdRepository bridgeRepo,
        ITextTrackRepository trackRepo,
        IEnumerable<ITextTrackProvider> providers,
        AssetPathService assetPaths,
        ILogger<TextTrackEnrichmentWorker> logger)
    {
        _assetRepo = assetRepo;
        _workRepo = workRepo;
        _canonicalRepo = canonicalRepo;
        _bridgeRepo = bridgeRepo;
        _trackRepo = trackRepo;
        _providers = providers;
        _assetPaths = assetPaths;
        _logger = logger;
    }

    public async Task EnrichAsync(Guid assetId, TextTrackKind kind, CancellationToken ct = default)
    {
        var asset = await _assetRepo.FindByIdAsync(assetId, ct).ConfigureAwait(false);
        if (asset is null)
        {
            _logger.LogDebug("Skipping {Kind} enrichment; asset {AssetId} was not found", kind, assetId);
            return;
        }

        var lineage = await _workRepo.GetLineageByAssetAsync(assetId, ct).ConfigureAwait(false);
        var mediaType = lineage?.MediaType ?? InferMediaType(asset.FilePathRoot);
        if (!IsRelevant(kind, mediaType))
            return;

        await ImportLocalSidecarsAsync(asset, kind, ct).ConfigureAwait(false);

        var existingPreferred = await _trackRepo.GetPreferredAsync(asset.Id, kind, null, ct).ConfigureAwait(false);
        if (existingPreferred?.IsUserOwned == true)
            return;

        var lookup = await BuildLookupAsync(asset, lineage, mediaType, kind, ct).ConfigureAwait(false);
        foreach (var provider in _providers.Where(p => p.Kind == kind && p.IsEnabled && p.CanHandle(mediaType)))
        {
            var candidates = await provider.SearchAsync(lookup, ct).ConfigureAwait(false);
            foreach (var candidate in candidates.OrderByDescending(c => c.Confidence).Take(1))
            {
                var download = await provider.DownloadAsync(candidate, ct).ConfigureAwait(false);
                if (download is null)
                    continue;

                var saved = await SaveDownloadAsync(asset, download, ct).ConfigureAwait(false);
                if (saved is not null)
                {
                    await _trackRepo.SetPreferredAsync(saved.Id, ct).ConfigureAwait(false);
                    if (kind == TextTrackKind.Subtitles && _assetPaths.ShouldKeepPreferredSubtitlesLocal)
                        await ExportPreferredSubtitleAsync(asset, saved, ct).ConfigureAwait(false);
                    return;
                }
            }
        }
    }

    private async Task ImportLocalSidecarsAsync(MediaAsset asset, TextTrackKind kind, CancellationToken ct)
    {
        var mediaPath = asset.FilePathRoot;
        if (string.IsNullOrWhiteSpace(mediaPath))
            return;

        var directory = Path.GetDirectoryName(mediaPath);
        var basename = Path.GetFileNameWithoutExtension(mediaPath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(basename) || !Directory.Exists(directory))
            return;

        var patterns = kind == TextTrackKind.Lyrics
            ? new[] { $"{basename}.lrc" }
            : new[] { $"{basename}.vtt", $"{basename}.srt", $"{basename}.*.vtt", $"{basename}.*.srt", $"{basename}.*.ass" };

        foreach (var pattern in patterns)
        {
            foreach (var path in Directory.EnumerateFiles(directory, pattern))
            {
                var extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
                var language = ExtractLanguage(path, basename);
                var localPath = path;
                var normalizedFormat = extension;

                if (kind == TextTrackKind.Subtitles && extension is "srt" or "ass")
                {
                    var normalized = SubtitleNormalizer.NormalizeToWebVtt(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false), extension);
                    localPath = _assetPaths.GetCentralTextTrackPath(asset.Id, "Subtitles", "local", language, ".vtt");
                    AssetPathService.EnsureDirectory(localPath);
                    await File.WriteAllTextAsync(localPath, normalized, ct).ConfigureAwait(false);
                    normalizedFormat = "vtt";
                }

                if (kind == TextTrackKind.Lyrics && extension == "lrc")
                {
                    var normalized = LrcParser.Normalize(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false));
                    localPath = _assetPaths.GetCentralTextTrackPath(asset.Id, "Lyrics", "local", language, ".lrc");
                    AssetPathService.EnsureDirectory(localPath);
                    await File.WriteAllTextAsync(localPath, normalized, ct).ConfigureAwait(false);
                }

                var track = new TextTrack
                {
                    AssetId = asset.Id,
                    Kind = kind,
                    Language = language,
                    Provider = "local",
                    Confidence = 1,
                    SourceFormat = extension,
                    NormalizedFormat = normalizedFormat,
                    LocalPath = localPath,
                    SidecarPath = path,
                    TimingMode = kind == TextTrackKind.Lyrics ? "Line" : "Cue",
                    IsPreferred = true,
                    IsUserOwned = true,
                };
                await _trackRepo.UpsertAsync(track, ct).ConfigureAwait(false);
                await _trackRepo.SetPreferredAsync(track.Id, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task<TextTrackLookup> BuildLookupAsync(MediaAsset asset, WorkLineage? lineage, MediaType mediaType, TextTrackKind kind, CancellationToken ct)
    {
        var entityIds = new List<Guid> { asset.Id };
        if (lineage is not null)
        {
            entityIds.Add(lineage.TargetForSelfScope);
            entityIds.Add(lineage.TargetForParentScope);
        }

        var canonicalGroups = await _canonicalRepo.GetByEntitiesAsync(entityIds.Distinct().ToList(), ct).ConfigureAwait(false);
        var bridgeGroups = await _bridgeRepo.GetByEntitiesAsync(entityIds.Distinct().ToList(), ct).ConfigureAwait(false);
        var values = canonicalGroups.Values.SelectMany(v => v)
            .GroupBy(v => v.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase);
        var bridgeIds = bridgeGroups.Values.SelectMany(v => v)
            .GroupBy(v => v.IdType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().IdValue, StringComparer.OrdinalIgnoreCase);

        foreach (var key in new[] { MetadataFieldConstants.SeasonNumber, MetadataFieldConstants.EpisodeNumber })
        {
            if (values.TryGetValue(key, out var value))
                bridgeIds[key] = value;
        }

        return new TextTrackLookup(
            asset,
            mediaType,
            First(values, MetadataFieldConstants.Title, MetadataFieldConstants.EpisodeTitle, MetadataFieldConstants.ShowName),
            First(values, MetadataFieldConstants.Artist, MetadataFieldConstants.Author),
            First(values, MetadataFieldConstants.Album, MetadataFieldConstants.Series, MetadataFieldConstants.ShowName),
            First(values, MetadataFieldConstants.Year),
            First(values, MetadataFieldConstants.Language) ?? (kind == TextTrackKind.Subtitles ? "en" : null),
            ParseDurationSeconds(First(values, MetadataFieldConstants.DurationField, MetadataFieldConstants.Runtime)),
            bridgeIds);
    }

    private async Task<TextTrack?> SaveDownloadAsync(MediaAsset asset, TextTrackDownload download, CancellationToken ct)
    {
        var candidate = download.Candidate;
        var normalized = candidate.Kind == TextTrackKind.Lyrics
            ? LrcParser.Normalize(download.Content)
            : SubtitleNormalizer.NormalizeToWebVtt(download.Content, download.SourceFormat);
        var extension = candidate.Kind == TextTrackKind.Lyrics ? ".lrc" : ".vtt";
        var kindName = candidate.Kind == TextTrackKind.Lyrics ? "Lyrics" : "Subtitles";
        var path = _assetPaths.GetCentralTextTrackPath(asset.Id, kindName, candidate.Provider, candidate.Language, extension);
        AssetPathService.EnsureDirectory(path);
        await File.WriteAllTextAsync(path, normalized, ct).ConfigureAwait(false);

        var track = new TextTrack
        {
            AssetId = asset.Id,
            Kind = candidate.Kind,
            Language = candidate.Language,
            Provider = candidate.Provider,
            Confidence = candidate.Confidence,
            SourceId = candidate.SourceId,
            SourceUrl = candidate.SourceUrl,
            SourceFormat = download.SourceFormat,
            NormalizedFormat = download.NormalizedFormat,
            LocalPath = path,
            TimingMode = candidate.Kind == TextTrackKind.Lyrics ? "Line" : "Cue",
            DurationMatchScore = candidate.DurationMatchScore,
            IsHearingImpaired = candidate.IsHearingImpaired,
        };

        await _trackRepo.UpsertAsync(track, ct).ConfigureAwait(false);
        return track;
    }

    private async Task ExportPreferredSubtitleAsync(MediaAsset asset, TextTrack track, CancellationToken ct)
    {
        if (!File.Exists(track.LocalPath))
            return;

        var exportPath = AssetPathService.BuildSubtitleSidecarPath(asset.FilePathRoot, track.Language, ".vtt");
        if (File.Exists(exportPath) && !string.Equals(exportPath, track.SidecarPath, StringComparison.OrdinalIgnoreCase))
            return;

        AssetPathService.EnsureDirectory(exportPath);
        File.Copy(track.LocalPath, exportPath, overwrite: true);
        track.SidecarPath = exportPath;
        await _trackRepo.UpsertAsync(track, ct).ConfigureAwait(false);
    }

    private static bool IsRelevant(TextTrackKind kind, MediaType mediaType) =>
        kind == TextTrackKind.Lyrics
            ? mediaType == MediaType.Music
            : mediaType is MediaType.Movies or MediaType.TV;

    private static MediaType InferMediaType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".mp3" or ".m4a" or ".flac" or ".wav" or ".ogg" ? MediaType.Music
            : ext is ".mp4" or ".mkv" or ".m4v" or ".webm" or ".avi" ? MediaType.Movies
            : MediaType.Unknown;
    }

    private static string ExtractLanguage(string path, string basename)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var suffix = name.Length > basename.Length ? name[(basename.Length + 1)..] : string.Empty;
        return string.IsNullOrWhiteSpace(suffix) ? "und" : suffix.Split('.', StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();
    }

    private static string? First(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
            if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        return null;
    }

    private static double? ParseDurationSeconds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            return seconds > 0 ? seconds : null;
        if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var span))
            return span.TotalSeconds;
        return null;
    }
}
