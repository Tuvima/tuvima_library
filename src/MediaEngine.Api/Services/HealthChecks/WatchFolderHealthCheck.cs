using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MediaEngine.Ingestion.Models;

namespace MediaEngine.Api.Services.HealthChecks;

public sealed class WatchFolderHealthCheck(IOptions<IngestionOptions> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var watchDirs = options.Value.EffectiveWatchDirectories;
        if (watchDirs.Count == 0)
            return Task.FromResult(HealthCheckResult.Degraded("Watch Folder is not configured."));

        var missing = watchDirs.Where(path => !Directory.Exists(path)).ToList();
        if (missing.Count > 0)
            return Task.FromResult(HealthCheckResult.Unhealthy($"Watch Folder does not exist: {string.Join(", ", missing)}"));

        return Task.FromResult(HealthCheckResult.Healthy($"Watch Folder is accessible: {string.Join(", ", watchDirs)}"));
    }
}
