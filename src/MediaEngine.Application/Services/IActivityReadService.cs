using MediaEngine.Application.ReadModels;
using MediaEngine.Contracts.Activity;
using MediaEngine.Contracts.Paging;

namespace MediaEngine.Application.Services;

public interface IActivityBatchReadService
{
    Task<PagedResponse<ActivityBatchSummaryDto>> GetBatchesAsync(
        ActivityBatchQuery query,
        CancellationToken ct = default);

    Task<IReadOnlyList<ActivityMediaTypeGroupDto>> GetGroupsAsync(
        Guid batchId,
        CancellationToken ct = default);

    Task<PagedResponse<ActivityBatchItemDto>> GetItemsAsync(
        Guid batchId,
        string? mediaType,
        int offset,
        int limit,
        string? sort,
        string? sortDirection,
        CancellationToken ct = default);

    Task<ActivityBatchItemDetailDto?> GetItemDetailAsync(
        Guid batchId,
        Guid assetId,
        CancellationToken ct = default);

    Task<PagedResponse<ActivityPersonAuditDto>> GetPeopleAsync(
        ActivityBatchQuery query,
        CancellationToken ct = default);
}
