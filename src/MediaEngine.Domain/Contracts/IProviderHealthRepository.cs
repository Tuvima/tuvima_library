namespace MediaEngine.Domain.Contracts;

using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;

/// <summary>
/// Persists provider health state in the database.
/// </summary>
public interface IProviderHealthRepository
{
    /// <summary>Get health record for a single provider.</summary>
    Task<ProviderHealthRecord?> GetAsync(string providerId, CancellationToken ct = default);

    /// <summary>Get health records for all providers.</summary>
    Task<IReadOnlyList<ProviderHealthRecord>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Get all providers currently in the Down state.</summary>
    Task<IReadOnlyList<ProviderHealthRecord>> GetDownProvidersAsync(CancellationToken ct = default);

    /// <summary>Insert or update a provider health record.</summary>
    Task UpsertAsync(ProviderHealthRecord record, CancellationToken ct = default);

    /// <summary>
    /// Record a successful request — resets consecutive failures,
    /// updates last_success_at, and transitions status to Healthy.
    /// Returns true if the provider was previously Down (recovery event).
    /// </summary>
    Task<bool> RecordSuccessAsync(string providerId, CancellationToken ct = default);

    /// <summary>
    /// Record a failed request — increments consecutive failures,
    /// updates last_failure_at, and transitions status as needed.
    /// Returns the new status after the failure.
    /// </summary>
    Task<ProviderHealthStatus> RecordFailureAsync(string providerId, string reason, CancellationToken ct = default);
}
