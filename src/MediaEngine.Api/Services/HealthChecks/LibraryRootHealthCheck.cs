using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MediaEngine.Ingestion.Models;

namespace MediaEngine.Api.Services.HealthChecks;

public sealed class LibraryRootHealthCheck(IOptions<IngestionOptions> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var root = options.Value.LibraryRoot;
        if (string.IsNullOrWhiteSpace(root))
            return Task.FromResult(HealthCheckResult.Degraded("Library Root is not configured."));

        if (!Directory.Exists(root))
            return Task.FromResult(HealthCheckResult.Unhealthy($"Library Root does not exist: {root}"));

        return Task.FromResult(HealthCheckResult.Healthy($"Library Root is accessible: {root}"));
    }
}
