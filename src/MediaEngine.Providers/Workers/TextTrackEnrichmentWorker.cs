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

public sealed record TextTrackEnrichmentResult(
    string Status,
    TextTrackKind Kind,
    int BeforeCount,
    int AfterCount,
    Guid? TrackId,
    string Message);

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
    private readonly ITextTrackExportService? _textTrackExportService;

    public TextTrackEnrichmentWorker(
        IMediaAssetRepository assetRepo,
        IWorkRepository workRepo,
        ICanonicalValueRepository canonicalRepo,
        IBridgeIdRepository bridgeRepo,
        ITextTrackRepository trackRepo,
        IEnumerable<ITextTrackProvider> providers,
        AssetPathService assetPaths,
        ILogger<TextTrackEnrichmentWorker> logger,
        ITextTrackExportService? textTrackExportService = null)
    {
        _assetRepo = assetRepo;
        _workRepo = workRepo;
        _canonicalRepo = canonicalRepo;
        _bridgeRepo = bridgeRepo;
        _trackRepo = trackRepo;
        _providers = providers;
        _assetPaths = assetPaths;
        _logger = logger;
        _textTrackExportService = textTrackExportService;
    }

    public async Task<TextTrackEnrichmentResult> EnrichAsync(Guid assetId, TextTrackKind kind, CancellationToken ct = default)
    {
        var asset = await _assetRepo.FindByIdAsync(assetId, ct).ConfigureAwait(false);
        if (asset is null)
        {
            _logger.LogDebug("Skipping {Kind} enrichment; asset {AssetId} was not found", kind, assetId);
            return new("AssetMissing", kind, 0, 0, null, "The owned media file could not be found.");
        }

        var lineage = await _workRepo.GetLineageByAssetAsync(assetId, ct).ConfigureAwait(false);
        var mediaType = lineage?.MediaType ?? InferMediaType(asset.FilePathRoot);
        if (!IsRelevant(kind, mediaType))
            return new("Unsupported", kind, 0, 0, null, $"{kind} are not supported for {mediaType}.");

        var before = await _trackRepo.GetByAssetAsync(assetId, kind, ct).ConfigureAwait(false);

        var importedLocal = await ImportLocalSidecarsAsync(asset, kind, ct).ConfigureAwait(false);

        var existingPreferred = await _trackRepo.GetPreferredAsync(asset.Id, kind, null, ct).ConfigureAwait(false);
        if (existingPreferred?.IsUserOwned == true)
        {
            var afterLocal = await _trackRepo.GetByAssetAsync(assetId, kind, ct).ConfigureAwait(false);
            return new(
                importedLocal ? "Updated" : "PreservedUserOwned",
                kind,
                before.Count,
                afterLocal.Count,
                existingPreferred.Id,
                importedLocal
                    ? "Local text tracks were refreshed and the user-owned choice was preserved."
                    : "The user-owned preferred text track was preserved.");
        }

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
                    if (kind == TextTrackKind.Subtitles
                        && _assetPaths.ShouldKeepPreferredSubtitlesLocal
                        && _textTrackExportService is not null)
                        await ExportPreferredSubtitleAsync(asset, saved, ct).ConfigureAwait(false);
                    var afterDownload = await _trackRepo.GetByAssetAsync(assetId, kind, ct).ConfigureAwait(false);
                    return new("Updated", kind, before.Count, afterDownload.Count, saved.Id,
                        $"{kind} were refreshed from {candidate.Provider}.");
                }
            }
        }

        var after = await _trackRepo.GetByAssetAsync(assetId, kind, ct).ConfigureAwait(false);
        return new(
            importedLocal ? "Updated" : "NoResult",
            kind,
            before.Count,
            after.Count,
            after.FirstOrDefault(track => track.IsPreferred)?.Id,
            importedLocal
                ? "Local text tracks were refreshed."
                : $"No matching {kind.ToString().ToLowerInvariant()} were found.");
    }

    private async Task<bool> ImportLocalSidecarsAsync(MediaAsset asset, TextTrackKind kind, CancellationToken ct)
    {
        var mediaPath = asset.FilePathRoot;
        if (string.IsNullOrWhiteSpace(mediaPath))
            return false;

        var directory = Path.GetDirectoryName(mediaPath);
        var basename = Path.GetFileNameWithoutExtension(mediaPath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(basename) || !Directory.Exists(directory))
            return false;

        var patterns = kind == TextTrackKind.Lyrics
            ? new[] { $"{basename}.lrc" }
            : new[] { $"{basename}.vtt", $"{basename}.srt", $"{basename}.*.vtt", $"{basename}.*.srt", $"{basename}.*.ass" };

        var imported = false;
        foreach (var pattern in patterns)
        {
            foreach (var path in Directory.EnumerateFiles(directory, pattern))
            {
                var extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
                var language = ExtractLanguage(path, basename);
                var identity = BuildTrackIdentity(asset.Id, kind, "local", path, language);
                var storageProvider = $"local-{Hashing.Sha256Hex(identity)[..12]}";
                var localPath = path;
                var normalizedFormat = extension;

                if (kind == TextTrackKind.Subtitles && extension is "srt" or "ass")
                {
                    var normalized = SubtitleNormalizer.NormalizeToWebVtt(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false), extension);
                    localPath = _assetPaths.GetCentralTextTrackPath(asset.Id, "Subtitles", storageProvider, language, ".vtt");
                    await WriteTextAtomicallyAsync(localPath, normalized, ct).ConfigureAwait(false);
                    normalizedFormat = "vtt";
                }

                if (kind == TextTrackKind.Lyrics && extension == "lrc")
                {
                    var normalized = LrcParser.Normalize(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false));
                    localPath = _assetPaths.GetCentralTextTrackPath(asset.Id, "Lyrics", storageProvider, language, ".lrc");
                    await WriteTextAtomicallyAsync(localPath, normalized, ct).ConfigureAwait(false);
                }

                var track = new TextTrack
                {
                    Id = Hashing.DeterministicGuid(identity),
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
                imported = true;
            }
        }

        return imported;
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
        var sourceIdentity = string.IsNullOrWhiteSpace(candidate.SourceId) ? candidate.SourceUrl : candidate.SourceId;
        var identity = BuildTrackIdentity(asset.Id, candidate.Kind, candidate.Provider, sourceIdentity, candidate.Language);
        var storageProvider = $"{candidate.Provider}-{Hashing.Sha256Hex(identity)[..12]}";
        var path = _assetPaths.GetCentralTextTrackPath(asset.Id, kindName, storageProvider, candidate.Language, extension);
        await WriteTextAtomicallyAsync(path, normalized, ct).ConfigureAwait(false);

        var track = new TextTrack
        {
            Id = Hashing.DeterministicGuid(identity),
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

    internal static string BuildTrackIdentity(
        Guid assetId,
        TextTrackKind kind,
        string provider,
        string? sourceIdentity,
        string language) =>
        $"text-track|{assetId:D}|{kind}|{provider.Trim().ToLowerInvariant()}|{sourceIdentity?.Trim().ToLowerInvariant()}|{language.Trim().ToLowerInvariant()}";

    private static async Task WriteTextAtomicallyAsync(string path, string content, CancellationToken ct)
    {
        AssetPathService.EnsureDirectory(path);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, ct).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private async Task ExportPreferredSubtitleAsync(MediaAsset asset, TextTrack track, CancellationToken ct)
    {
        var exportPath = await _textTrackExportService!.ExportPreferredSubtitleAsync(asset, track, ct)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(exportPath))
            return;

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
