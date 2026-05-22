using MediaEngine.Storage.Services;

namespace MediaEngine.Api.Services;

public sealed class HierarchyRepairStartupService : BackgroundService
{
    private readonly WorkHierarchyMaintenanceService _hierarchyMaintenance;
    private readonly ILogger<HierarchyRepairStartupService> _logger;

    public HierarchyRepairStartupService(
        WorkHierarchyMaintenanceService hierarchyMaintenance,
        ILogger<HierarchyRepairStartupService> logger)
    {
        ArgumentNullException.ThrowIfNull(hierarchyMaintenance);
        ArgumentNullException.ThrowIfNull(logger);
        _hierarchyMaintenance = hierarchyMaintenance;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var result = await _hierarchyMaintenance.RepairLegacyTvAndMusicAsync(stoppingToken);
            if (result.ParentsCreated > 0 || result.ChildrenReparented > 0 || result.EmptyParentsRemoved > 0)
            {
                _logger.LogInformation(
                    "Hierarchy startup repair complete: {ParentsCreated} parents created, {ChildrenReparented} children reparented, {EmptyParentsRemoved} empty parents removed",
                    result.ParentsCreated,
                    result.ChildrenReparented,
                    result.EmptyParentsRemoved);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hierarchy startup repair failed");
        }
    }
}
