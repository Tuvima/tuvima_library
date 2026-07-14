using MediaEngine.AI.Configuration;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Models;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services;

/// <summary>
/// Builds persisted taste profiles in a bounded, sequential weekly batch.
/// </summary>
public sealed class TasteProfileBackgroundService : BackgroundService
{
    private const string FeatureKey = "taste_profile";
    private const string ModelId = "text_fast";
    private const string PromptVersion = "taste-profile-v1";

    private readonly AiSettings _settings;
    private readonly IConfigurationLoader _configLoader;
    private readonly ITasteProfiler _profiler;
    private readonly IProfileRepository _profiles;
    private readonly ITasteProfileRepository _tasteProfiles;
    private readonly IAiFeaturePersistenceRepository _featurePersistence;
    private readonly ILogger<TasteProfileBackgroundService> _logger;

    public TasteProfileBackgroundService(
        AiSettings settings,
        IConfigurationLoader configLoader,
        ITasteProfiler profiler,
        IProfileRepository profiles,
        ITasteProfileRepository tasteProfiles,
        IAiFeaturePersistenceRepository featurePersistence,
        ILogger<TasteProfileBackgroundService> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(configLoader);
        ArgumentNullException.ThrowIfNull(profiler);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(tasteProfiles);
        ArgumentNullException.ThrowIfNull(featurePersistence);
        ArgumentNullException.ThrowIfNull(logger);

        _settings = settings;
        _configLoader = configLoader;
        _profiler = profiler;
        _profiles = profiles;
        _tasteProfiles = tasteProfiles;
        _featurePersistence = featurePersistence;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await BuildProfilesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TasteProfileService failed");
            }

            var maintenance = _configLoader.LoadMaintenance();
            var cron = maintenance.Schedules.TryGetValue("taste_profile_update", out var schedule)
                ? schedule
                : "0 5 * * 0";
            var delay = CronScheduler.UntilNext(cron, TimeSpan.FromDays(7));
            _logger.LogInformation("TasteProfileService: next run in {Delay}", delay);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task BuildProfilesAsync(CancellationToken ct)
    {
        var profiles = await _profiles.GetAllAsync(ct);
        var batchLimit = Math.Clamp(_settings.EnrichmentBatchSize, 1, 25);
        var processed = 0;

        foreach (var profile in profiles.Take(batchLimit))
        {
            ct.ThrowIfCancellationRequested();
            var priorState = await _featurePersistence.GetAiFeatureStateAsync(
                profile.Id,
                FeatureKey,
                ct);
            if (priorState?.CanAttempt(DateTimeOffset.UtcNow) == false)
                continue;

            string? fingerprint = null;
            try
            {
                var buildResult = await _profiler.GetProfileAsync(profile.Id, ct);
                fingerprint = buildResult.InputFingerprint;
                if (priorState?.IsCurrent(fingerprint) == true)
                    continue;

                await _tasteProfiles.ReplaceAiProfileAsync(
                    new TasteProfilePersistenceRequest(
                        buildResult,
                        FeatureKey,
                        WellKnownProviders.AiProvider,
                        ClaimConfidence.AiDescription,
                        PublishThreshold: 0.65,
                        ReviewThreshold: 0.75,
                        ModelId,
                        PromptVersion),
                    ct);
                processed++;
                if (buildResult.Status == TasteProfileBuildStatus.InsufficientData)
                    _logger.LogInformation(
                        "TasteProfileService: profile {ProfileId} has insufficient data: {Reason}",
                        profile.Id,
                        buildResult.Reason);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var failure = await _featurePersistence.RecordAiFeatureFailureAsync(
                    new AiFeatureFailureRequest(
                        profile.Id,
                        FeatureKey,
                        WellKnownProviders.AiProvider,
                        ModelId,
                        PromptVersion,
                        fingerprint ?? AiFeatureFingerprint.Compute(profile.Id.ToString("N")),
                        ex.Message),
                    ct);
                _logger.LogWarning(ex, "TasteProfileService: failed profile {ProfileId}", profile.Id);
                if (failure.Status == AiFeatureStatus.Poisoned)
                    _logger.LogError(
                        "TasteProfileService: quarantined poison profile {ProfileId} after {Attempts} attempts",
                        profile.Id,
                        failure.Attempts);
            }
        }

        _logger.LogInformation("TasteProfileService: persisted {Count} profile(s)", processed);
    }

}
