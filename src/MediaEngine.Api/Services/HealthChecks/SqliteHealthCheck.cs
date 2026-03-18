using Microsoft.Extensions.Diagnostics.HealthChecks;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.HealthChecks;

public sealed class SqliteHealthCheck(IDatabaseConnection db) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var conn = db.CreateConnection();
            return Task.FromResult(HealthCheckResult.Healthy("SQLite database is accessible."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("SQLite database is not accessible.", ex));
        }
    }
}
