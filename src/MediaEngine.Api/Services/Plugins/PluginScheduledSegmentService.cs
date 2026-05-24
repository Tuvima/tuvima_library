using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.Plugins;

public sealed class PluginScheduledSegmentService : BackgroundService
{
    private const string DefaultSchedule = "0 2 * * *";
    private const int DefaultBatchSize = 25;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfigurationLoader _configLoader;
    private readonly PluginCatalog _catalog;
    private readonly PluginJobStateService _jobs;
    private readonly ILogger<PluginScheduledSegmentService> _logger;

    public PluginScheduledSegmentService(
        IServiceScopeFactory scopeFactory,
        IConfigurationLoader configLoader,
        PluginCatalog catalog,
        PluginJobStateService jobs,
        ILogger<PluginScheduledSegmentService> logger)
    {
        _scopeFactory = scopeFactory;
        _configLoader = configLoader;
        _catalog = catalog;
        _jobs = jobs;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunScheduledPassAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Scheduled plugin segment pass failed");
            }

            var maintenance = _configLoader.LoadMaintenance();
            var cron = maintenance.Schedules.TryGetValue("plugin_segment_detection", out var configured)
                ? configured
                : DefaultSchedule;
            var delay = CronScheduler.UntilNext(string.IsNullOrWhiteSpace(cron) ? DefaultSchedule : cron, TimeSpan.FromHours(24));
            _logger.LogInformation("PluginScheduledSegmentService: next run in {Delay}", delay);
            await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<PluginJobSnapshot>> RunScheduledPassAsync(CancellationToken ct = default)
    {
        var enabledSegmentPlugins = _catalog.List()
            .Where(p => p.Enabled && p.LoadError is null && p.Capabilities.OfType<MediaEngine.Plugins.IPlaybackSegmentDetector>().Any())
            .ToList();

        if (enabledSegmentPlugins.Count == 0)
            return [];

        using var scope = _scopeFactory.CreateScope();
        var assets = await scope.ServiceProvider
            .GetRequiredService<IMediaAssetRepository>()
            .ListByStatusAsync(AssetStatus.Normal, ct)
            .ConfigureAwait(false);
        var detector = scope.ServiceProvider.GetRequiredService<PluginSegmentDetectionService>();
        var tracker = scope.ServiceProvider.GetService<IMediaOperationTracker>();
        var completed = new List<PluginJobSnapshot>();

        foreach (var plugin in enabledSegmentPlugins)
        {
            var maxAssets = ReadInt(plugin.Settings, "scheduled_batch_size", DefaultBatchSize);
            var job = _jobs.Start(plugin.Manifest.Id, "playback-segment-detection");
            MediaOperation? operation = null;
            if (tracker is not null)
            {
                operation = await tracker.EnsureQueuedAsync(new MediaOperation
                {
                    OperationType = MediaOperationType.PluginPlaybackSegmentDetection,
                    OperationKind = MediaOperationKind.Plugin,
                    CapabilityId = CapabilityId.PluginCommercialSkip,
                    PluginId = plugin.Manifest.Id,
                    PluginVersion = plugin.Manifest.Version,
                    Status = MediaOperationStatus.Queued,
                    Stage = MediaOperationStage.Queued,
                    QueueName = "plugin",
                    ItemsTotal = Math.Max(1, maxAssets),
                    IdempotencyKey = $"plugin:{plugin.Manifest.Id}:playback-segment-detection:{plugin.Manifest.Version}:{DateTimeOffset.UtcNow:O}"
                }, ct).ConfigureAwait(false);
                await tracker.UpdateStageAsync(operation.Id, MediaOperationStage.Analyzing, 0, "Starting playback segment detection pass.", null, ct).ConfigureAwait(false);
            }
            var scanned = 0;
            var written = 0;
            try
            {
                foreach (var asset in assets.Where(a => File.Exists(a.FilePathRoot)).Take(Math.Max(1, maxAssets)))
                {
                    ct.ThrowIfCancellationRequested();
                    var before = await detector.ListExistingAsync(asset.Id, ct).ConfigureAwait(false);
                    var after = await detector.DetectAsync(asset.Id, plugin.Manifest.Id, ct).ConfigureAwait(false);
                    scanned++;
                    written += Math.Max(0, after.Count - before.Count);
                    if (tracker is not null && operation is not null)
                    {
                        var progress = (int)Math.Round(scanned * 100.0 / Math.Max(1, maxAssets));
                        await tracker.UpdateStageAsync(operation.Id, MediaOperationStage.Analyzing, progress, "Analyzed media asset for playback segments.", new
                        {
                            asset_id = asset.Id,
                            scanned,
                            written
                        }, ct).ConfigureAwait(false);
                    }
                }

                _jobs.Complete(job.Id, scanned, written);
                if (tracker is not null && operation is not null)
                {
                    if (written > 0)
                        await tracker.MarkSucceededAsync(operation.Id, $"Detected {written} playback segment(s) across {scanned} asset(s).", new { scanned, written }, ct).ConfigureAwait(false);
                    else
                        await tracker.MarkNoResultAsync(operation.Id, "No playback segments detected.", new { scanned, written }, ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _jobs.Fail(job.Id, ex.Message, scanned, written);
                if (tracker is not null && operation is not null)
                    await tracker.MarkFailedAsync(operation.Id, ex, terminal: false, ct).ConfigureAwait(false);
                _logger.LogWarning(ex, "Scheduled plugin segment detection failed for {PluginId}", plugin.Manifest.Id);
            }

            completed.Add(_jobs.List(plugin.Manifest.Id).First(j => j.Id == job.Id));
        }

        return completed;
    }

    private static int ReadInt(IReadOnlyDictionary<string, System.Text.Json.JsonElement> settings, string key, int fallback)
    {
        if (!settings.TryGetValue(key, out var value))
            return fallback;

        return value.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Number when value.TryGetInt32(out var parsed) => parsed,
            System.Text.Json.JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
            _ => fallback,
        };
    }
}
