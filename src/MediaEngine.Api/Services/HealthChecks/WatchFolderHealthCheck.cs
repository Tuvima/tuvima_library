using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MediaEngine.Ingestion.Models;

namespace MediaEngine.Api.Services.HealthChecks;

public sealed class WatchFolderHealthCheck(IOptions<IngestionOptions> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var watchDir = options.Value.WatchDirectory;
        if (string.IsNullOrWhiteSpace(watchDir))
            return Task.FromResult(HealthCheckResult.Degraded("Watch Folder is not configured."));

        if (!Directory.Exists(watchDir))
            return Task.FromResult(HealthCheckResult.Unhealthy($"Watch Folder does not exist: {watchDir}"));

        return Task.FromResult(HealthCheckResult.Healthy($"Watch Folder is accessible: {watchDir}"));
    }
}
