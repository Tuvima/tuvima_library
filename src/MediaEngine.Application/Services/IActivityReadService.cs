using MediaEngine.Application.ReadModels;
using MediaEngine.Contracts.Activity;
using MediaEngine.Contracts.Paging;

namespace MediaEngine.Application.Services;

public interface IActivityBatchReadService
{
    Task<ActivityBatchSummaryDto?> GetBatchAsync(
        Guid batchId,
        CancellationToken ct = default);

    Task<PagedResponse<ActivityBatchSummaryDto>> GetBatchesAsync(
        ActivityBatchQuery query,
        CancellationToken ct = default);

    Task<IReadOnlyList<ActivityMediaTypeGroupDto>> GetGroupsAsync(
        Guid batchId,
        CancellationToken ct = default);

    Task<ActivityBatchInsightsDto> GetInsightsAsync(
        Guid batchId,
        CancellationToken ct = default);

    Task<PagedResponse<ActivityBatchItemDto>> GetItemsAsync(
        Guid batchId,
        string? mediaType,
        string? search,
        string? status,
        string? source,
        int offset,
        int limit,
        string? sort,
        string? sortDirection,
        CancellationToken ct = default);

    Task<PagedResponse<ActivityTechnicalEventDto>> GetEventsAsync(
        Guid batchId,
        string? search,
        string? eventType,
        string? result,
        int offset,
        int limit,
        CancellationToken ct = default);

    Task<ActivityBatchItemDetailDto?> GetItemDetailAsync(
        Guid batchId,
        Guid assetId,
        CancellationToken ct = default);

    Task<PagedResponse<ActivityPersonAuditDto>> GetPeopleAsync(
        ActivityBatchQuery query,
        CancellationToken ct = default);
}
