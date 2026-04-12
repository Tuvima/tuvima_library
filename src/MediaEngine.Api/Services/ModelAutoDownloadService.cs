using MediaEngine.AI.Configuration;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services;

/// <summary>
/// One-shot background service that automatically downloads any missing AI model
/// files 10 seconds after the Engine starts.
///
/// Checks whether all required models are already present; if they are, the
/// service exits immediately with no network activity.  If any are missing, it
/// downloads each role sequentially (TextFast → TextQuality → TextScholar → Audio),
/// logging progress and tolerating individual failures so a failed download never
/// prevents the remaining models from being attempted.
///
/// The optional CJK model (TextCjk / Qwen 2.5) is only downloaded automatically
/// when the user has configured CJK languages (ja, ko, zh, zh-TW) in their language
/// preferences.  Users without CJK content are not forced to download 2 GB they do
/// not need.  The model can always be downloaded manually from Settings → AI Models.
/// </summary>
public sealed class ModelAutoDownloadService : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(10);

    // CJK model is handled separately — it is conditional on language preferences.
    private static readonly AiModelRole[] DownloadOrder =
    [
        AiModelRole.TextFast,
        AiModelRole.TextQuality,
        AiModelRole.TextScholar,
        AiModelRole.Audio,
    ];

    private static readonly HashSet<string> CjkLanguageCodes =
        new(StringComparer.OrdinalIgnoreCase) { "ja", "ko", "zh", "zh-TW" };

    private readonly IModelDownloadManager _downloadManager;
    private readonly AiSettings _aiSettings;
    private readonly ILogger<ModelAutoDownloadService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public ModelAutoDownloadService(
        IModelDownloadManager              downloadManager,
        AiSettings                        aiSettings,
        ILogger<ModelAutoDownloadService>  logger,
        IServiceScopeFactory               scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(downloadManager);
        ArgumentNullException.ThrowIfNull(aiSettings);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(scopeFactory);

        _downloadManager = downloadManager;
        _aiSettings      = aiSettings;
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
        if (_aiSettings.DevSkipDownload)
        {
            _logger.LogInformation(
                "ModelAutoDownloadService: dev_skip_download is enabled; skipping automatic AI model downloads");
            return;
        }

        if (_downloadManager.AreAllModelsReady())
        {
            _logger.LogInformation("ModelAutoDownloadService: all AI models are present — nothing to download");
            return;
        }

        _logger.LogInformation("ModelAutoDownloadService: AI models not found — starting automatic download...");

        foreach (var role in DownloadOrder)
        {
            if (ct.IsCancellationRequested) break;
            await DownloadRoleIfNeededAsync(role, ct);
        }

        // CJK model (Qwen 2.5) is optional — only auto-download when the user has
        // configured CJK languages.  Users with purely Latin-script libraries are
        // not forced to download an extra 2 GB.
        if (!ct.IsCancellationRequested && ShouldDownloadCjkModel())
        {
            _logger.LogInformation(
                "ModelAutoDownloadService: CJK language detected in preferences — including text_cjk model");
            await DownloadRoleIfNeededAsync(AiModelRole.TextCjk, ct);
        }
        else if (!ct.IsCancellationRequested)
        {
            _logger.LogInformation(
                "ModelAutoDownloadService: no CJK language in preferences — skipping text_cjk model (can be downloaded manually from Settings → AI Models)");
        }

        _logger.LogInformation("ModelAutoDownloadService: automatic download sequence complete");
    }

    private async Task DownloadRoleIfNeededAsync(AiModelRole role, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;

        var status = _downloadManager.GetStatus(role);

        // Skip roles that are already downloaded or currently downloading.
        if (status.State is AiModelState.Ready
                         or AiModelState.Loaded
                         or AiModelState.Downloading)
        {
            _logger.LogDebug(
                "ModelAutoDownloadService: skipping {Role} — already in state {State}",
                role, status.State);
            return;
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

    /// <summary>
    /// Returns true when the user's language preferences include at least one CJK
    /// language code (ja, ko, zh, zh-TW).  Reads core config via a transient scope.
    /// </summary>
    private bool ShouldDownloadCjkModel()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var configLoader = scope.ServiceProvider.GetRequiredService<IConfigurationLoader>();
            var core = configLoader.LoadCore();
            var lang = core.Language;

            // Check display and metadata languages.
            if (IsCjk(lang.Display) || IsCjk(lang.Metadata))
                return true;

            // Check any additional languages the user has configured.
            return lang.Additional.Any(IsCjk);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ModelAutoDownloadService: could not read language preferences to determine CJK download — skipping text_cjk");
            return false;
        }
    }

    private static bool IsCjk(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        // Normalise: "zh-TW" → compare both "zh-TW" and the base "zh".
        var normalised = code.Trim();
        return CjkLanguageCodes.Contains(normalised)
            || CjkLanguageCodes.Contains(normalised.Split('-', '_')[0]);
    }
}
