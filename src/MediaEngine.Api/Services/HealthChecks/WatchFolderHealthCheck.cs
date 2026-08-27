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
            return Task.FromResult(HealthCheckResult.Degraded(
                "Watch Folder is not configured.",
                data: new Dictionary<string, object> { ["category"] = "storage", ["required"] = false }));

        var missing = watchDirs.Where(path => !Directory.Exists(path)).ToList();
        if (missing.Count > 0)
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Watch Folder does not exist: {string.Join(", ", missing)}",
                data: new Dictionary<string, object>
                {
                    ["category"] = "storage",
                    ["required"] = false,
                    ["missing"] = string.Join(", ", missing),
                }));

        try
        {
            foreach (var path in watchDirs)
                _ = Directory.EnumerateFileSystemEntries(path).Take(1).ToList();
            return Task.FromResult(HealthCheckResult.Healthy(
                $"Watch Folder is readable: {string.Join(", ", watchDirs)}",
                new Dictionary<string, object>
                {
                    ["category"] = "storage",
                    ["required"] = false,
                    ["paths"] = string.Join(", ", watchDirs),
                }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "One or more Watch Folders cannot be read.",
                ex,
                new Dictionary<string, object> { ["category"] = "storage", ["required"] = false }));
        }
    }
}
