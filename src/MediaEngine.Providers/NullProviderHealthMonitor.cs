using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Providers;

/// <summary>
/// No-op health monitor for testing and contexts where health tracking is not needed.
/// </summary>
public sealed class NullProviderHealthMonitor : IProviderHealthMonitor
{
    public static readonly NullProviderHealthMonitor Instance = new();

    public Task ReportSuccessAsync(string providerId, CancellationToken ct = default) => Task.CompletedTask;
    public Task ReportFailureAsync(string providerId, string reason, CancellationToken ct = default) => Task.CompletedTask;
    public bool IsDown(string providerId) => false;
    public ProviderHealthStatus GetStatus(string providerId) => ProviderHealthStatus.Healthy;
}
