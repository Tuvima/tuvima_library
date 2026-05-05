using System.Text.Json;
using MediaEngine.Contracts.Playback;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Plugins;

namespace MediaEngine.Api.Services.Plugins;

public sealed class PluginSegmentDetectionService
{
    private readonly PluginCatalog _catalog;
    private readonly IPlaybackSegmentRepository _segments;
    private readonly IMediaAssetRepository _assets;
    private readonly IPluginToolRuntime _tools;
    private readonly IPluginAiClient _ai;
    private readonly ILogger<PluginSegmentDetectionService> _logger;

    public PluginSegmentDetectionService(
        PluginCatalog catalog,
        IPlaybackSegmentRepository segments,
        IMediaAssetRepository assets,
        IPluginToolRuntime tools,
        IPluginAiClient ai,
        ILogger<PluginSegmentDetectionService> logger)
    {
        _catalog = catalog;
        _segments = segments;
        _assets = assets;
        _tools = tools;
        _ai = ai;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PlaybackSegmentDto>> DetectAsync(Guid assetId, string? pluginId, CancellationToken ct = default)
    {
        var asset = await _assets.FindByIdAsync(assetId, ct).ConfigureAwait(false);
        if (asset is null)
            return [];

        var context = new PluginMediaAssetContext
        {
            AssetId = asset.Id,
            FilePath = asset.FilePathRoot,
            MediaType = InferMediaType(asset.FilePathRoot),
        };

        var registrations = string.IsNullOrWhiteSpace(pluginId)
            ? _catalog.List().Where(r => r.Enabled && r.LoadError is null)
            : _catalog.List().Where(r => string.Equals(r.Manifest.Id, pluginId, StringComparison.OrdinalIgnoreCase));

        var detected = new List<PlaybackSegment>();
        foreach (var registration in registrations)
        {
            if (!registration.Enabled || registration.LoadError is not null)
                continue;

            var temp = Path.Combine(Path.GetTempPath(), "tuvima-plugins", registration.Manifest.Id, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                var execution = new PluginExecutionContext(registration.Manifest.Id, registration.Settings, temp, _tools, _ai);
                foreach (var detector in registration.Capabilities.OfType<IPlaybackSegmentDetector>())
                {
                    if (!detector.CanAnalyze(context))
                        continue;

                    var results = await detector.AnalyzeAsync(context, execution, ct).ConfigureAwait(false);
                    detected.AddRange(results.Select(result => new PlaybackSegment
                    {
                        Id = Guid.NewGuid(),
                        AssetId = assetId,
                        Kind = result.Kind,
                        StartSeconds = result.StartSeconds,
                        EndSeconds = result.EndSeconds,
                        Confidence = Math.Clamp(result.Confidence, 0, 1),
                        Source = result.Source,
                        PluginId = registration.Manifest.Id,
                        IsSkippable = result.IsSkippable,
                        ReviewStatus = result.ReviewStatus,
                    }));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Plugin segment detection failed for {PluginId} asset {AssetId}", registration.Manifest.Id, assetId);
            }
            finally
            {
                TryDeleteDirectory(temp);
            }
        }

        await _segments.UpsertBatchAsync(assetId, detected, ct).ConfigureAwait(false);
        return (await _segments.ListByAssetAsync(assetId, ct).ConfigureAwait(false)).Select(ToDto).ToList();
    }

    public static PlaybackSegmentDto ToDto(PlaybackSegment segment) => new()
    {
        Id = segment.Id,
        AssetId = segment.AssetId,
        Kind = segment.Kind,
        StartSeconds = segment.StartSeconds,
        EndSeconds = segment.EndSeconds,
        Confidence = segment.Confidence,
        Source = segment.Source,
        PluginId = segment.PluginId,
        IsSkippable = segment.IsSkippable,
        ReviewStatus = segment.ReviewStatus,
    };

    private static string InferMediaType(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.ToLowerInvariant() switch
        {
            ".mp4" or ".m4v" or ".mkv" or ".webm" or ".avi" => "Movies",
            ".mp3" or ".m4a" or ".m4b" or ".aac" or ".flac" or ".ogg" or ".wav" => "Audio",
            _ => "Unknown",
        };
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch { }
    }
}

