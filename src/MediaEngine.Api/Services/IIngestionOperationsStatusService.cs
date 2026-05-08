using MediaEngine.Api.Models;

namespace MediaEngine.Api.Services;

public interface IIngestionOperationsStatusService
{
    Task<IngestionOperationsSnapshotDto> GetSnapshotAsync(CancellationToken ct = default);
}
