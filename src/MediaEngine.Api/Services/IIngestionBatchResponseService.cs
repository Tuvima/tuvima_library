using MediaEngine.Contracts.Ingestion;

namespace MediaEngine.Api.Services;

public interface IIngestionBatchResponseService
{
    Task<IReadOnlyList<IngestionBatchResponse>> GetRecentAsync(
        int limit,
        CancellationToken ct = default);

    Task<IngestionBatchResponse?> GetByIdAsync(
        Guid id,
        CancellationToken ct = default);
}
