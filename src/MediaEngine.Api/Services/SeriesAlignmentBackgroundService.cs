using MediaEngine.AI.Configuration;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Models;

namespace MediaEngine.Api.Services;

/// <summary>
/// Background service that detects works with a series name but no position,
/// and uses the AI SeriesAligner to infer their position.
/// Runs on a configurable cron schedule (default: 3 AM daily).
/// </summary>
public sealed class SeriesAlignmentBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AiSettings _settings;
    private readonly ILogger<SeriesAlignmentBackgroundService> _logger;

    public SeriesAlignmentBackgroundService(
        IServiceScopeFactory scopeFactory,
        AiSettings settings,
        ILogger<SeriesAlignmentBackgroundService> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _settings     = settings;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAlignmentAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SeriesAlignmentService batch failed");
            }

            var delay = CronScheduler.UntilNext(
                _settings.Scheduling.SeriesCheckCron,
                TimeSpan.FromHours(24));

            _logger.LogInformation("SeriesAlignmentService: next run in {Delay}", delay);
            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task RunAlignmentAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var aligner       = scope.ServiceProvider.GetRequiredService<ISeriesAligner>();
        var canonicalRepo = scope.ServiceProvider.GetRequiredService<ICanonicalValueRepository>();
        var registryRepo  = scope.ServiceProvider.GetRequiredService<IRegistryRepository>();

        _logger.LogInformation("SeriesAlignmentService: scanning for unpositioned series works");

        // Page through the registry to find candidates (up to 200 per batch).
        var page = await registryRepo.GetPageAsync(
            new RegistryQuery(Offset: 0, Limit: 200, Sort: "newest"), ct);

        // First pass: collect entities that have 'series' but not 'series_position'.
        var candidates = new List<(Guid EntityId, string SeriesName, string Title)>();
        foreach (var item in page.Items)
        {
            if (ct.IsCancellationRequested) break;

            var canonicals = await canonicalRepo.GetByEntityAsync(item.EntityId, ct);

            var series = canonicals.FirstOrDefault(c =>
                string.Equals(c.Key, "series", StringComparison.OrdinalIgnoreCase));
            if (series is null) continue;

            var hasPosition = canonicals.Any(c =>
                string.Equals(c.Key, "series_position", StringComparison.OrdinalIgnoreCase));
            if (hasPosition) continue;

            var title = canonicals.FirstOrDefault(c =>
                string.Equals(c.Key, "title", StringComparison.OrdinalIgnoreCase));

            candidates.Add((item.EntityId, series.Value, title?.Value ?? item.Title));
        }

        if (candidates.Count == 0)
        {
            _logger.LogInformation("SeriesAlignmentService: no unpositioned series works found");
            return;
        }

        _logger.LogInformation(
            "SeriesAlignmentService: found {Count} unpositioned series work(s)", candidates.Count);

        // Group by series name so we can pass sibling titles to the aligner.
        var bySeries = candidates
            .GroupBy(c => c.SeriesName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        int aligned = 0;
        foreach (var (seriesName, members) in bySeries)
        {
            if (ct.IsCancellationRequested) break;

            var siblingTitles = members.Select(m => m.Title).ToList();

            foreach (var (entityId, _, workTitle) in members)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    var position = await aligner.InferPositionAsync(
                        workTitle, seriesName, siblingTitles, ct);

                    if (position is not null)
                    {
                        await canonicalRepo.UpsertBatchAsync(
                            [new CanonicalValue
                            {
                                EntityId     = entityId,
                                Key          = "series_position",
                                Value        = position.Value.ToString(),
                                LastScoredAt = DateTimeOffset.UtcNow,
                            }], ct);

                        aligned++;
                        _logger.LogDebug(
                            "SeriesAlignmentService: {Title} → position {Position} in '{Series}'",
                            workTitle, position, seriesName);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "SeriesAlignmentService: failed to infer position for entity {Id}", entityId);
                }
            }
        }

        _logger.LogInformation("SeriesAlignmentService: aligned {Count} work(s)", aligned);
    }
}
