namespace MediaEngine.Contracts.Maintenance;

public sealed record StorageMaintenanceResultDto(
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    bool DryRun,
    IReadOnlyList<StorageMaintenanceStepResultDto> Steps)
{
    public int TotalAffectedRows => Steps.Sum(step => step.AffectedRows);
}

public sealed record StorageMaintenanceStepResultDto(
    string Name,
    int AffectedRows,
    string Detail);
