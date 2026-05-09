using MediaEngine.Web.Models.ViewDTOs;

namespace MediaEngine.Web.Services.Integration;

public enum SetupReadinessStatus
{
    Complete,
    NeedsAttention,
    Optional,
    Unavailable,
    InProgress,
}

public sealed record SetupPathReadiness(
    string Label,
    string Path,
    bool IsConfigured,
    PathTestResultDto? Probe,
    bool RequiresWriteAccess)
{
    public bool Exists => Probe?.Exists == true;
    public bool HasRead => Probe?.HasRead == true;
    public bool HasWrite => Probe?.HasWrite == true;
    public bool IsReady => IsConfigured && Exists && HasRead && (!RequiresWriteAccess || HasWrite);
}

public sealed record SetupProviderReadiness(
    IReadOnlyList<ProviderStatusDto> Providers,
    bool StatusLoaded)
{
    public IReadOnlyList<ProviderStatusDto> EnabledProviders =>
        Providers.Where(provider => provider.Enabled).ToList();

    public IReadOnlyList<ProviderStatusDto> MissingCredentialProviders =>
        EnabledProviders.Where(provider => provider.RequiresApiKey && !provider.HasApiKey).ToList();

    public bool HasUsableZeroKeyProvider =>
        EnabledProviders.Any(provider => provider.IsZeroKey || !provider.RequiresApiKey);

    public bool HasReachableEnabledProvider =>
        EnabledProviders.Any(provider => provider.IsReachable || string.Equals(provider.HealthStatus, "healthy", StringComparison.OrdinalIgnoreCase));
}

public sealed record SetupAiReadiness(
    HardwareProfileDto? HardwareProfile,
    ResourceSnapshotDto? Resources,
    bool ProfileLoaded,
    bool ResourcesLoaded);

public sealed record SetupReadinessSnapshot(
    SystemStatusViewModel? EngineStatus,
    FolderSettingsDto? FolderSettings,
    SetupPathReadiness LibraryRoot,
    SetupPathReadiness WatchFolder,
    SetupProviderReadiness Providers,
    SetupAiReadiness LocalAi,
    int PendingReviewCount,
    IngestionOperationsSnapshotViewModel? Operations,
    string? Error)
{
    public bool EngineReachable => EngineStatus?.IsHealthy == true;
    public bool FoldersReady => LibraryRoot.IsReady && WatchFolder.IsReady;
    public bool HasScanEverRun => Operations?.Summary.LastSuccessfulScanTime is not null || Operations?.RecentBatches.Count > 0;
    public bool IsIngestionActive => Operations?.ActiveJobs.Count > 0;
    public bool CanScan => EngineReachable && FoldersReady && !IsIngestionActive;
}

public sealed class SetupReadinessService
{
    private readonly UIOrchestratorService _orchestrator;
    private readonly IEngineApiClient _api;
    private readonly ILogger<SetupReadinessService> _logger;

    public SetupReadinessService(
        UIOrchestratorService orchestrator,
        IEngineApiClient api,
        ILogger<SetupReadinessService> logger)
    {
        _orchestrator = orchestrator;
        _api = api;
        _logger = logger;
    }

    public async Task<SetupReadinessSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        SystemStatusViewModel? engineStatus = null;
        FolderSettingsDto? folderSettings = null;
        IReadOnlyList<ProviderStatusDto> providers = [];
        HardwareProfileDto? aiProfile = null;
        ResourceSnapshotDto? resources = null;
        IngestionOperationsSnapshotViewModel? operations = null;
        var reviewCount = 0;
        string? error = null;

        try
        {
            engineStatus = await _api.GetSystemStatusAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Setup readiness could not reach the Engine.");
            error = "The Dashboard cannot reach the Engine.";
        }

        if (engineStatus?.IsHealthy != true)
        {
            return BuildSnapshot(
                engineStatus,
                folderSettings,
                libraryProbe: null,
                watchProbe: null,
                new SetupProviderReadiness(providers, StatusLoaded: false),
                new SetupAiReadiness(aiProfile, resources, ProfileLoaded: false, ResourcesLoaded: false),
                reviewCount,
                operations,
                error);
        }

        try
        {
            folderSettings = await _orchestrator.GetFolderSettingsAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Setup readiness could not load folder settings.");
        }

        var libraryProbe = await ProbePathAsync(folderSettings?.LibraryRoot, ct);
        var watchProbe = await ProbePathAsync(folderSettings?.WatchDirectory, ct);

        var providerStatusLoaded = false;
        try
        {
            providers = await _orchestrator.GetProviderStatusAsync(ct);
            providerStatusLoaded = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Setup readiness could not load provider status.");
        }

        var aiProfileLoaded = false;
        try
        {
            aiProfile = await _orchestrator.GetAiProfileAsync(ct);
            aiProfileLoaded = aiProfile is not null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Setup readiness could not load local AI profile.");
        }

        var resourcesLoaded = false;
        try
        {
            resources = await _orchestrator.GetResourceSnapshotAsync(ct);
            resourcesLoaded = resources is not null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Setup readiness could not load local AI resource snapshot.");
        }

        try
        {
            reviewCount = await _orchestrator.RefreshReviewCountAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Setup readiness could not load review count.");
        }

        try
        {
            operations = await _api.GetIngestionOperationsSnapshotAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Setup readiness could not load ingestion operations.");
        }

        return BuildSnapshot(
            engineStatus,
            folderSettings,
            libraryProbe,
            watchProbe,
            new SetupProviderReadiness(providers, providerStatusLoaded),
            new SetupAiReadiness(aiProfile, resources, aiProfileLoaded, resourcesLoaded),
            reviewCount,
            operations,
            error);
    }

    private async Task<PathTestResultDto?> ProbePathAsync(string? path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return await _orchestrator.TestPathAsync(path, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Setup readiness could not test path {Path}.", path);
            return null;
        }
    }

    private static SetupReadinessSnapshot BuildSnapshot(
        SystemStatusViewModel? engineStatus,
        FolderSettingsDto? folderSettings,
        PathTestResultDto? libraryProbe,
        PathTestResultDto? watchProbe,
        SetupProviderReadiness providers,
        SetupAiReadiness ai,
        int reviewCount,
        IngestionOperationsSnapshotViewModel? operations,
        string? error)
    {
        var libraryPath = folderSettings?.LibraryRoot ?? string.Empty;
        var watchPath = folderSettings?.WatchDirectory ?? string.Empty;

        return new SetupReadinessSnapshot(
            engineStatus,
            folderSettings,
            new SetupPathReadiness("Library Root", libraryPath, !string.IsNullOrWhiteSpace(libraryPath), libraryProbe, RequiresWriteAccess: true),
            new SetupPathReadiness("Watch Folder", watchPath, !string.IsNullOrWhiteSpace(watchPath), watchProbe, RequiresWriteAccess: true),
            providers,
            ai,
            reviewCount,
            operations,
            error);
    }
}
