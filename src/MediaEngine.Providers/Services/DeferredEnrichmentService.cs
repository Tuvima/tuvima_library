using System.Text.Json;
using Microsoft.Extensions.Logging;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Models;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Background worker that processes deferred Pass 2 (Universe Lookup)
/// enrichment requests.
///
/// <list type="bullet">
///   <item><b>Idle detection:</b> checks <see cref="IHydrationPipelineService.PendingCount"/>
///     every <c>pass2_idle_delay_seconds</c>. Processes only when the ingestion
///     pipeline is idle (count = 0).</item>
///   <item><b>Rate limiting:</b> delays <c>pass2_rate_limit_ms</c> between each item
///     to respect external API rate limits.</item>
///   <item><b>Manual trigger:</b> <see cref="TriggerImmediateProcessingAsync"/> bypasses
///     idle detection for user-initiated processing.</item>
///   <item><b>Nightly sweep:</b> processes stale items (older than
///     <c>pass2_stale_threshold_hours</c>) on a configurable schedule.</item>
/// </list>
///
/// Follows the same pattern as <see cref="HydrationPipelineService"/>: the
/// background loop is started with <c>Task.Run</c> and stopped via a shared
/// <see cref="CancellationTokenSource"/> — no <c>Microsoft.Extensions.Hosting</c>
/// dependency required.
///
/// Spec: §3.24 — Two-Pass Enrichment Architecture.
/// </summary>
public sealed class DeferredEnrichmentService : IDeferredEnrichmentService, IAsyncDisposable
{
    private readonly IDeferredEnrichmentRepository _repo;
    private readonly IHydrationPipelineService _pipeline;
    private readonly IConfigurationLoader _config;
    private readonly ILogger<DeferredEnrichmentService> _logger;

    private readonly CancellationTokenSource _cts = new();
    private readonly Task _backgroundLoop;

    /// <summary>Signalled when the user clicks "Run Pass 2 Now".</summary>
    private readonly ManualResetEventSlim _manualTrigger = new(false);

    /// <summary>Tracks the last nightly sweep execution to avoid re-running within the same hour.</summary>
    private DateTime _lastNightlyRun = DateTime.MinValue;

    public DeferredEnrichmentService(
        IDeferredEnrichmentRepository repo,
        IHydrationPipelineService pipeline,
        IConfigurationLoader config,
        ILogger<DeferredEnrichmentService> logger)
    {
        _repo     = repo     ?? throw new ArgumentNullException(nameof(repo));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _config   = config   ?? throw new ArgumentNullException(nameof(config));
        _logger   = logger   ?? throw new ArgumentNullException(nameof(logger));

        _backgroundLoop = Task.Run(ExecuteAsync);
    }

    /// <inheritdoc/>
    public int PendingCount
    {
        get
        {
            try { return _repo.CountPendingAsync().GetAwaiter().GetResult(); }
            catch { return 0; }
        }
    }

    /// <inheritdoc/>
    public async Task<int> TriggerImmediateProcessingAsync(CancellationToken ct = default)
    {
        var count = await _repo.CountPendingAsync(ct).ConfigureAwait(false);
        if (count > 0)
        {
            _logger.LogInformation("Manual Pass 2 trigger: {Count} pending items", count);
            _manualTrigger.Set();
        }
        return count;
    }

    // ── Background loop ────────────────────────────────────────────────────────

    private async Task ExecuteAsync()
    {
        // Short startup delay to let the rest of the app initialise.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), _cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _logger.LogInformation("DeferredEnrichmentService started");

        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                var settings = LoadSettings();

                if (!settings.TwoPassEnabled)
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), _cts.Token).ConfigureAwait(false);
                    continue;
                }

                // ── Manual trigger check ──────────────────────────────────
                if (_manualTrigger.IsSet)
                {
                    _manualTrigger.Reset();
                    _logger.LogInformation("Processing Pass 2 queue (manual trigger)");
                    await ProcessBatchAsync(settings, _cts.Token).ConfigureAwait(false);
                    continue;
                }

                // ── Nightly sweep check ───────────────────────────────────
                if (IsNightlySweepDue(settings))
                {
                    _logger.LogInformation("Nightly sweep triggered for stale Pass 2 items");
                    _lastNightlyRun = DateTime.Now;
                    await ProcessStaleAsync(settings, _cts.Token).ConfigureAwait(false);
                    continue;
                }

                // ── Idle check ────────────────────────────────────────────
                if (_pipeline.PendingCount == 0)
                {
                    var pending = await _repo.CountPendingAsync(_cts.Token).ConfigureAwait(false);
                    if (pending > 0)
                    {
                        _logger.LogDebug("Pipeline idle, processing Pass 2 queue ({Count} pending)", pending);
                        await ProcessBatchAsync(settings, _cts.Token).ConfigureAwait(false);
                        continue;
                    }
                }

                // Nothing to do — wait before next check.
                var delay = Math.Max(settings.Pass2IdleDelaySeconds, 1);
                await Task.Delay(TimeSpan.FromSeconds(delay), _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_cts.Token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeferredEnrichmentService loop error");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("DeferredEnrichmentService stopped");
    }

    // ── Processing ─────────────────────────────────────────────────────────────

    private async Task ProcessBatchAsync(HydrationSettings settings, CancellationToken ct)
    {
        var batch = await _repo.GetPendingAsync(settings.Pass2BatchSize, ct).ConfigureAwait(false);
        if (batch.Count == 0) return;

        _logger.LogInformation("Pass 2 batch: {Count} items", batch.Count);
        var processed = 0;

        foreach (var item in batch)
        {
            if (ct.IsCancellationRequested) break;

            // Pause if Pass 1 work arrived (ingestion takes priority).
            if (_pipeline.PendingCount > 0 && !_manualTrigger.IsSet)
            {
                _logger.LogInformation(
                    "Pass 2 paused — Pass 1 work arrived ({Processed}/{Total} processed)",
                    processed, batch.Count);
                break;
            }

            await ProcessOneAsync(item, ct).ConfigureAwait(false);
            processed++;

            // Rate limiting between items.
            if (settings.Pass2RateLimitMs > 0 && !ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(settings.Pass2RateLimitMs, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("Pass 2 batch complete: {Processed}/{Total} processed",
            processed, batch.Count);
    }

    /// <summary>
    /// Re-checks items matched at "work" level for edition-aware media types.
    /// Retries bridge resolution to see if a Wikidata edition entity has been created.
    /// Called by the nightly sweep or weekly sync.
    /// </summary>
    private async Task RecheckEditionsAsync(CancellationToken ct)
    {
        // TODO: Query works WHERE match_level = 'work'
        //   AND media_type IN (edition_aware_media_types from config)
        //   AND wikidata_checked_at < DateTimeOffset.UtcNow.AddDays(-recheckInterval)
        // For each: load bridge IDs, retry ResolveBridgeAsync
        // If found: update match_level = 'edition', link edition QID
        _logger.LogDebug("Edition re-check: stub — not yet implemented");
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private async Task ProcessStaleAsync(HydrationSettings settings, CancellationToken ct)
    {
        var threshold = TimeSpan.FromHours(Math.Max(settings.Pass2StaleThresholdHours, 1));
        var allStale = await _repo.GetStaleAsync(threshold, settings.Pass2BatchSize, ct).ConfigureAwait(false);

        // Filter out items waiting for down providers — recovery flush handles those.
        var stale = allStale
            .Where(i => i.FailureType != ProviderFailureType.ProviderDown)
            .ToList();

        if (stale.Count == 0)
        {
            _logger.LogDebug("Nightly sweep: no stale items");
            return;
        }

        _logger.LogInformation("Nightly sweep: {Count} stale items", stale.Count);

        foreach (var item in stale)
        {
            if (ct.IsCancellationRequested) break;

            await ProcessOneAsync(item, ct).ConfigureAwait(false);

            if (settings.Pass2RateLimitMs > 0 && !ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(settings.Pass2RateLimitMs, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task ProcessOneAsync(DeferredEnrichmentRequest item, CancellationToken ct)
    {
        // Skip items waiting for a provider that's still down.
        // The ProviderHealthMonitorService handles recovery flush for these.
        if (item.FailureType == ProviderFailureType.ProviderDown
            && !string.IsNullOrEmpty(item.FailedProviderName))
        {
            _logger.LogDebug("Skipping entity {EntityId} — waiting for provider {Provider}",
                item.EntityId, item.FailedProviderName);
            return;
        }

        try
        {
            var hints = DeserializeHints(item.HintsJson);

            var request = new HarvestRequest
            {
                EntityId              = item.EntityId,
                EntityType            = EntityType.MediaAsset,
                MediaType             = item.MediaType,
                Hints                 = hints,
                PreResolvedQid        = item.WikidataQid,
                Pass                  = HydrationPass.Universe,
                SuppressActivityEntry = true,
            };

            await _pipeline.RunSynchronousAsync(request, ct).ConfigureAwait(false);
            await _repo.MarkProcessedAsync(item.Id, ct).ConfigureAwait(false);

            _logger.LogDebug("Pass 2 complete for entity {EntityId} (QID: {Qid})",
                item.EntityId, item.WikidataQid ?? "none");
        }
        catch (OperationCanceledException)
        {
            // Propagate cancellation — don't swallow it.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pass 2 failed for entity {EntityId}", item.EntityId);
            // Leave as Pending — will be retried on next cycle.
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private HydrationSettings LoadSettings()
    {
        try
        {
            return _config.LoadHydration();
        }
        catch
        {
            return new HydrationSettings();
        }
    }

    /// <summary>
    /// Simple nightly sweep check: parses hour and minute from the cron expression
    /// and checks if the current local time is within a 10-minute window after the
    /// scheduled time, and we haven't already run this hour today.
    /// </summary>
    private bool IsNightlySweepDue(HydrationSettings settings)
    {
        try
        {
            var parts = settings.Pass2NightlyCron.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return false;

            if (!int.TryParse(parts[0], out var minute) || !int.TryParse(parts[1], out var hour))
                return false;

            var now = DateTime.Now;

            // Check if we're in the right hour and minute window (within 10 minutes).
            if (now.Hour != hour) return false;
            if (now.Minute < minute || now.Minute > minute + 9) return false;

            // Don't re-run if we already ran this hour today.
            if (_lastNightlyRun.Date == now.Date && _lastNightlyRun.Hour == now.Hour)
                return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyDictionary<string, string> DeserializeHints(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (dict is null)
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Rebuild with case-insensitive comparer.
            return new Dictionary<string, string>(dict, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    // ── IAsyncDisposable ───────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _manualTrigger.Dispose();

        try
        {
            await _backgroundLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }

        _cts.Dispose();
    }
}
