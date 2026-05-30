namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Runs database and generated-cache housekeeping that is safe to trigger manually
/// or from the nightly maintenance schedule.
/// </summary>
public interface IStorageMaintenanceService
{
    Task<StorageMaintenanceResult> RunAsync(
        StorageMaintenanceRequest request,
        CancellationToken ct = default);
}

public sealed record StorageMaintenanceRequest(
    bool DryRun = false,
    int? SearchCacheMaxAgeDays = null,
    int? ImageCacheRetentionDays = null,
    int? ClaimCompactionBatchSize = null);

public sealed record StorageMaintenanceResult(
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    bool DryRun,
    IReadOnlyList<StorageMaintenanceStepResult> Steps)
{
    public int TotalAffectedRows => Steps.Sum(step => step.AffectedRows);
}

public sealed record StorageMaintenanceStepResult(
    string Name,
    int AffectedRows,
    string Detail);
