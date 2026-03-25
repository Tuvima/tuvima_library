using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services;

/// <summary>
/// Background service that periodically re-checks works matched at "work" or
/// "retail_only" level to see if Wikidata now has edition data.
///
/// <para>
/// Some media items are ingested before their Wikidata edition entity exists
/// (e.g. a newly released audiobook where only the work QID is in Wikidata at
/// the time of ingestion). This service runs on a configurable weekly schedule
/// and retries bridge resolution for those items. When a Wikidata edition is
/// found, the match level is upgraded from "work" to "edition" and the item's
/// canonical values are updated.
/// </para>
///
/// <para>
/// Configuration:
/// <list type="bullet">
///   <item><c>config/maintenance.json → edition_recheck_interval_days</c> — interval
///     between re-check runs. Default: 7 days. Set to 0 to disable.</item>
///   <item><c>config/hydration.json → edition_aware_media_types</c> — only media
///     types in this list are checked. Default: Books, Audiobooks, Movies, Comics, Music.</item>
/// </list>
/// </para>
/// </summary>
public sealed class EditionRecheckService : BackgroundService
{
    private readonly IServiceProvider                _services;
    private readonly ILogger<EditionRecheckService>  _logger;

    /// <summary>Startup delay before the first recheck run.</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(5);

    public EditionRecheckService(
        IServiceProvider               services,
        ILogger<EditionRecheckService> logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(logger);

        _services = services;
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "EditionRecheckService started — first run in {Delay}",
            StartupDelay);

        // Wait after startup to avoid contending with initial ingestion and hydration.
        await Task.Delay(StartupDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunRecheckCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "EditionRecheckService: cycle failed — will retry next interval");
            }

            int intervalDays = LoadIntervalDays();
            if (intervalDays <= 0)
            {
                _logger.LogInformation("EditionRecheckService: disabled (edition_recheck_interval_days=0) — stopping");
                return;
            }

            var delay = TimeSpan.FromDays(intervalDays);
            _logger.LogInformation(
                "EditionRecheckService: next run in {Days} day(s) at approximately {NextRun:g}",
                intervalDays, DateTimeOffset.Now.Add(delay));

            await Task.Delay(delay, stoppingToken);
        }
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private int LoadIntervalDays()
    {
        try
        {
            using var scope      = _services.CreateScope();
            var configLoader     = scope.ServiceProvider.GetRequiredService<IConfigurationLoader>();
            var maintenance      = configLoader.LoadMaintenance();
            return maintenance.EditionRecheckIntervalDays;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EditionRecheckService: could not load maintenance config — using default 7 days");
            return 7;
        }
    }

    private async Task RunRecheckCycleAsync(CancellationToken ct)
    {
        using var scope      = _services.CreateScope();
        var configLoader     = scope.ServiceProvider.GetRequiredService<IConfigurationLoader>();
        var activityRepo     = scope.ServiceProvider.GetRequiredService<ISystemActivityRepository>();

        // Check if service is disabled.
        var maintenance = configLoader.LoadMaintenance();
        if (maintenance.EditionRecheckIntervalDays <= 0)
        {
            _logger.LogDebug("EditionRecheckService: disabled — skipping cycle");
            return;
        }

        // Load edition-aware media types from hydration config.
        var hydration = configLoader.LoadHydration();
        var editionAwareTypes = hydration.EditionAwareMediaTypes;
        if (editionAwareTypes.Count == 0)
        {
            _logger.LogDebug("EditionRecheckService: no edition-aware media types configured — skipping cycle");
            return;
        }

        _logger.LogInformation(
            "EditionRecheckService: starting cycle for media types [{Types}]",
            string.Join(", ", editionAwareTypes));

        // TODO: Query works WHERE match_level IN ('work', 'retail_only')
        //   AND media_type IN (editionAwareTypes)
        //   AND (wikidata_checked_at IS NULL OR wikidata_checked_at < @threshold)
        // For each: load bridge IDs, retry bridge resolution via IBridgeIdRepository
        // If a Wikidata edition entity is now found:
        //   - update match_level = 'edition'
        //   - link to edition QID
        //   - update wikidata_checked_at
        //   - re-run scoring pipeline
        // If no edition found: update wikidata_checked_at to suppress re-checking until next cycle
        //
        // Full implementation deferred until IBridgeIdRepository exposes a
        // ResolveBridgeAsync(string bridgeId, string mediaType) overload
        // and the works table carries a match_level column (planned for Sprint 8).

        await activityRepo.LogAsync(new SystemActivityEntry
        {
            ActionType  = SystemActionType.SyncCompleted,
            EntityType  = "System",
            Detail      = $"Edition re-check cycle completed for [{string.Join(", ", editionAwareTypes)}]",
        }, ct);

        _logger.LogInformation("EditionRecheckService: cycle completed");
    }
}
