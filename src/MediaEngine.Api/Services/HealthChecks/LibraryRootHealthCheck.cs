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
            return Task.FromResult(HealthCheckResult.Degraded(
                "Library Root is not configured.",
                data: new Dictionary<string, object> { ["category"] = "storage", ["required"] = false }));

        if (!Directory.Exists(root))
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Library Root does not exist: {root}",
                data: new Dictionary<string, object> { ["category"] = "storage", ["required"] = true }));

        var probe = Path.Combine(root, $".tuvima-readiness-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(probe, "readiness");
            File.Delete(probe);
            return Task.FromResult(HealthCheckResult.Healthy(
                $"Library Root is readable and writable: {root}",
                new Dictionary<string, object>
                {
                    ["category"] = "storage",
                    ["required"] = true,
                    ["path"] = root,
                    ["write_access"] = "ok",
                }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Library Root is not writable: {root}",
                ex,
                new Dictionary<string, object> { ["category"] = "storage", ["required"] = true, ["path"] = root }));
        }
        finally
        {
            try { if (File.Exists(probe)) File.Delete(probe); }
            catch (Exception) { }
        }
    }
}
