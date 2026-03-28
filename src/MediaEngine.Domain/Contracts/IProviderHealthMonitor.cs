namespace MediaEngine.Domain.Contracts;

using MediaEngine.Domain.Enums;

/// <summary>
/// Lightweight interface that adapters use to report request outcomes.
/// The monitor updates health state and schedules probes as needed.
/// </summary>
public interface IProviderHealthMonitor
{
    /// <summary>Report that a request to the provider succeeded.</summary>
    Task ReportSuccessAsync(string providerId, CancellationToken ct = default);

    /// <summary>Report that a request to the provider failed.</summary>
    Task ReportFailureAsync(string providerId, string reason, CancellationToken ct = default);

    /// <summary>Check whether a provider is currently known to be down.</summary>
    bool IsDown(string providerId);

    /// <summary>Check current health status for a provider.</summary>
    ProviderHealthStatus GetStatus(string providerId);
}
