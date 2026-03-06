using System.Diagnostics;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services;

/// <summary>
/// Background service that periodically scans all Normal-status assets and
/// verifies their files still exist on disk. Missing files are logged and
/// their directory artifacts (cover.jpg, library.xml, empty folders) are
/// cleaned up.
///
/// Also exposes <see cref="ReconcileAsync"/> for manual trigger via API.
///
/// Follows the same loop pattern as <see cref="ActivityPruningService"/>.
/// </summary>
public sealed class LibraryReconciliationService : BackgroundService
{
    private readonly IMediaAssetRepository       _assetRepo;
    private readonly IMetadataClaimRepository     _claimRepo;
    private readonly ICanonicalValueRepository    _canonicalRepo;
    private readonly ISystemActivityRepository    _activityRepo;
    private readonly IConfigurationLoader         _configLoader;
    private readonly ILogger<LibraryReconciliationService> _logger;

    public LibraryReconciliationService(
        IMediaAssetRepository       assetRepo,
        IMetadataClaimRepository    claimRepo,
        ICanonicalValueRepository   canonicalRepo,
        ISystemActivityRepository   activityRepo,
        IConfigurationLoader        configLoader,
        ILogger<LibraryReconciliationService> logger)
    {
        _assetRepo     = assetRepo;
        _claimRepo     = claimRepo;
        _canonicalRepo = canonicalRepo;
        _activityRepo  = activityRepo;
        _configLoader  = configLoader;
        _logger        = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LibraryReconciliationService started");

        // Initial delay to let the rest of the app start.
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var maintenance = _configLoader.LoadMaintenance();
                var intervalHours = maintenance.ReconciliationIntervalHours;

                if (intervalHours > 0)
                {
                    await ReconcileAsync(stoppingToken);
                    await Task.Delay(TimeSpan.FromHours(intervalHours), stoppingToken);
                }
                else
                {
                    // Disabled — check again in 1 hour in case config changes.
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Library reconciliation failed; will retry next cycle");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }

    /// <summary>
    /// Scans all Normal-status assets, verifies files exist on disk,
    /// and cleans up any orphans found.
    /// </summary>
    public async Task<ReconciliationResult> ReconcileAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var assets = await _assetRepo.ListByStatusAsync(AssetStatus.Normal, ct);
        int missingCount = 0;

        foreach (var asset in assets)
        {
            ct.ThrowIfCancellationRequested();

            if (File.Exists(asset.FilePathRoot))
                continue;

            _logger.LogInformation(
                "Reconciliation: file missing for asset {Id} at {Path}",
                asset.Id, asset.FilePathRoot);

            // Clean filesystem artifacts.
            var editionFolder = Path.GetDirectoryName(asset.FilePathRoot);
            if (!string.IsNullOrEmpty(editionFolder) && Directory.Exists(editionFolder))
            {
                SafeDeleteFile(Path.Combine(editionFolder, "cover.jpg"));
                SafeDeleteFile(Path.Combine(editionFolder, "library.xml"));
                TryDeleteEmptyDirectory(editionFolder);

                var hubFolder = Path.GetDirectoryName(editionFolder);
                if (!string.IsNullOrEmpty(hubFolder) && Directory.Exists(hubFolder))
                {
                    SafeDeleteFile(Path.Combine(hubFolder, "library.xml"));
                    TryDeleteEmptyDirectory(hubFolder);
                }
            }

            // Delete DB records.
            await _claimRepo.DeleteByEntityAsync(asset.Id, ct);
            await _canonicalRepo.DeleteByEntityAsync(asset.Id, ct);
            await _assetRepo.DeleteAsync(asset.Id, ct);

            // Log per-file activity.
            await _activityRepo.LogAsync(new SystemActivityEntry
            {
                ActionType = SystemActionType.ReconciliationMissing,
                EntityId   = asset.Id,
                EntityType = "MediaAsset",
                Detail     = $"Missing file detected: {Path.GetFileName(asset.FilePathRoot)}",
            }, ct);

            missingCount++;
        }

        sw.Stop();

        // Log completion.
        await _activityRepo.LogAsync(new SystemActivityEntry
        {
            ActionType  = SystemActionType.ReconciliationCompleted,
            EntityType  = "System",
            Detail      = $"Reconciliation complete — scanned {assets.Count}, {missingCount} missing",
            ChangesJson = $"{{\"total_scanned\":{assets.Count},\"missing_count\":{missingCount},\"elapsed_ms\":{sw.ElapsedMilliseconds}}}",
        }, ct);

        _logger.LogInformation(
            "Reconciliation complete: {Total} assets scanned, {Missing} missing, {Elapsed}ms",
            assets.Count, missingCount, sw.ElapsedMilliseconds);

        return new ReconciliationResult(assets.Count, missingCount, sw.ElapsedMilliseconds);
    }

    private static void SafeDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
                Directory.Delete(path);
        }
        catch (IOException) { }
    }
}

/// <summary>
/// Result of a library reconciliation scan.
/// </summary>
public sealed record ReconciliationResult(int TotalScanned, int MissingCount, long ElapsedMs);
