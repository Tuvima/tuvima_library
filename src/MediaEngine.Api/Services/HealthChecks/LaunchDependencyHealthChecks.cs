using MediaEngine.AI.Configuration;
using MediaEngine.AI.Infrastructure;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MediaEngine.Api.Services.HealthChecks;

public sealed class ConfigurationHealthCheck(IConfigurationLoader configuration) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _ = configuration.LoadCore();
            _ = configuration.LoadLibraries();
            _ = configuration.LoadPipelines();
            var providers = configuration.LoadAllProviders();
            var ai = configuration.LoadAi<AiSettings>()
                ?? throw new InvalidOperationException("config/ai.json is required.");
            var aiErrors = AiSettingsValidator.Validate(ai);
            if (aiErrors.Count > 0)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    $"AI configuration has {aiErrors.Count} validation error(s).",
                    data: Data(true, ("errors", string.Join(" | ", aiErrors.Select(error => error.Message))))));
            }

            return Task.FromResult(HealthCheckResult.Healthy(
                "Required configuration files are valid.",
                Data(true, ("providers_loaded", providers.Count))));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Required configuration is invalid.",
                ex,
                Data(true, ("error", ex.Message))));
        }
    }

    private static Dictionary<string, object> Data(bool required, params (string Key, object Value)[] values)
    {
        var data = new Dictionary<string, object> { ["category"] = "configuration", ["required"] = required };
        foreach (var (key, value) in values) data[key] = value;
        return data;
    }
}

public sealed class ModelReadinessHealthCheck(ModelInventory inventory, AiSettings settings) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        inventory.Refresh();
        var statuses = inventory.GetAllStatuses();
        var unavailable = statuses
            .Where(status => status.State is AiModelState.NotDownloaded or AiModelState.Error)
            .Select(status => $"{status.Role}:{status.State}")
            .ToArray();
        var data = new Dictionary<string, object>
        {
            ["category"] = "models",
            ["required"] = false,
            ["configured"] = statuses.Count,
            ["unavailable"] = string.Join(", ", unavailable),
            ["automatic_download"] = settings.DevSkipDownload ? "bypassed" : "enabled",
        };

        return Task.FromResult(unavailable.Length == 0
            ? HealthCheckResult.Healthy("Configured AI model artifacts are ready.", data)
            : HealthCheckResult.Degraded(
                $"{unavailable.Length} AI model role(s) are unavailable; dependent features will wait without consuming poison attempts.",
                data: data));
    }
}

public sealed class ProviderReadinessHealthCheck(
    IConfigurationLoader configuration,
    IProviderHealthMonitor healthMonitor) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var providers = configuration.LoadAllProviders();
            var enabled = providers.Where(provider => provider.Enabled).ToArray();
            var down = enabled
                .Where(provider => healthMonitor.GetStatus(provider.Name) == ProviderHealthStatus.Down)
                .Select(provider => provider.Name)
                .ToArray();
            var data = new Dictionary<string, object>
            {
                ["category"] = "providers",
                ["required"] = false,
                ["enabled"] = enabled.Length,
                ["disabled"] = providers.Count - enabled.Length,
                ["down"] = string.Join(", ", down),
            };
            return Task.FromResult(down.Length == 0
                ? HealthCheckResult.Healthy("Enabled metadata providers are configured.", data)
                : HealthCheckResult.Degraded(
                    $"{down.Length} enabled provider(s) are unavailable; their work will remain retryable.",
                    data: data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Provider configuration could not be evaluated.",
                ex,
                new Dictionary<string, object> { ["category"] = "providers", ["required"] = true }));
        }
    }
}

public sealed class WorkerReadinessHealthCheck(IEnumerable<IHostedService> hostedServices) : IHealthCheck
{
    private static readonly string[] RequiredWorkerNames =
    [
        "LibraryReconciliationService",
        "RetailMatchHostedService",
        "WikidataBridgeHostedService",
        "QuickHydrationHostedService",
    ];

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var services = hostedServices.ToArray();
        var missing = RequiredWorkerNames
            .Where(name => services.All(service => !string.Equals(service.GetType().Name, name, StringComparison.Ordinal)))
            .ToArray();
        var requiredServices = services
            .Where(service => RequiredWorkerNames.Contains(service.GetType().Name, StringComparer.Ordinal))
            .OfType<BackgroundService>()
            .ToArray();
        var failed = requiredServices
            .Where(service => service.ExecuteTask is { IsFaulted: true })
            .Select(service => service.GetType().Name)
            .ToArray();
        var stopped = requiredServices
            .Where(service => service.ExecuteTask is { IsCompletedSuccessfully: true })
            .Select(service => service.GetType().Name)
            .ToArray();
        var data = new Dictionary<string, object>
        {
            ["category"] = "workers",
            ["required"] = true,
            ["registered"] = services.Length,
            ["missing"] = string.Join(", ", missing),
            ["faulted"] = string.Join(", ", failed),
            ["stopped"] = string.Join(", ", stopped),
        };

        return Task.FromResult(missing.Length == 0 && failed.Length == 0 && stopped.Length == 0
            ? HealthCheckResult.Healthy("Required background workers are running.", data)
            : HealthCheckResult.Unhealthy("One or more required background workers are missing, stopped, or faulted.", data: data));
    }
}
