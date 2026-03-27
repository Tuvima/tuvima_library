using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Api.Services;

/// <summary>
/// One-shot background service that automatically downloads any missing AI model
/// files 10 seconds after the Engine starts.
///
/// Checks whether all required models are already present; if they are, the
/// service exits immediately with no network activity.  If any are missing, it
/// downloads each role sequentially (TextFast → TextQuality → Audio), logging
/// progress and tolerating individual failures so a failed download never
/// prevents the remaining models from being attempted.
/// </summary>
public sealed class ModelAutoDownloadService : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(10);

    private static readonly AiModelRole[] DownloadOrder =
    [
        AiModelRole.TextFast,
        AiModelRole.TextQuality,
        AiModelRole.TextScholar,
        AiModelRole.Audio,
    ];

    private readonly IModelDownloadManager _downloadManager;
    private readonly ILogger<ModelAutoDownloadService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public ModelAutoDownloadService(
        IModelDownloadManager              downloadManager,
        ILogger<ModelAutoDownloadService>  logger,
        IServiceScopeFactory               scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(downloadManager);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(scopeFactory);

        _downloadManager = downloadManager;
        _logger          = logger;
        _scopeFactory    = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "ModelAutoDownloadService: waiting {Seconds}s for Engine to fully start",
            StartupDelay.TotalSeconds);

        await Task.Delay(StartupDelay, stoppingToken);

        if (stoppingToken.IsCancellationRequested) return;

        try
        {
            await RunAutoDownloadAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Shutdown during download — no action needed.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ModelAutoDownloadService: auto-download failed unexpectedly");
        }

        // Service is done — it only runs once on startup.
    }

    // ── Download logic ────────────────────────────────────────────────────────

    private async Task RunAutoDownloadAsync(CancellationToken ct)
    {
        if (_downloadManager.AreAllModelsReady())
        {
            _logger.LogInformation("ModelAutoDownloadService: all AI models are present — nothing to download");
            return;
        }

        _logger.LogInformation("ModelAutoDownloadService: AI models not found — starting automatic download...");

        foreach (var role in DownloadOrder)
        {
            if (ct.IsCancellationRequested) break;

            var status = _downloadManager.GetStatus(role);

            // Skip roles that are already downloaded or currently downloading.
            if (status.State is AiModelState.Ready
                             or AiModelState.Loaded
                             or AiModelState.Downloading)
            {
                _logger.LogDebug(
                    "ModelAutoDownloadService: skipping {Role} — already in state {State}",
                    role, status.State);
                continue;
            }

            try
            {
                _logger.LogInformation(
                    "ModelAutoDownloadService: starting download for {Role} ({SizeMB} MB)...",
                    role, status.SizeMB);

                await _downloadManager.StartDownloadAsync(role, ct);

                _logger.LogInformation(
                    "ModelAutoDownloadService: download initiated for {Role}",
                    role);
            }
            catch (OperationCanceledException)
            {
                throw; // Let shutdown propagate.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "ModelAutoDownloadService: download failed for {Role} — continuing with remaining models",
                    role);
            }
        }

        _logger.LogInformation("ModelAutoDownloadService: all AI models ready");
    }
}
