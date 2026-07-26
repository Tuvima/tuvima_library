using MediaEngine.Application.ReadModels;
using MediaEngine.Contracts.Collections;
using MediaEngine.Contracts.Library;
using MediaEngine.Contracts.Paging;

namespace MediaEngine.Application.Services;

public interface ILibraryCurationReadService
{
    Task<IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, Guid>>> ResolveBatchEditTargetsAsync(
        IReadOnlyCollection<Guid> entityIds,
        IReadOnlyCollection<string> fieldKeys,
        CancellationToken ct = default);

    Task<IReadOnlyList<UniverseCandidateReadModel>> GetUniverseCandidatesAsync(CancellationToken ct = default);
    Task<Guid?> FindOwnedAssetIdForWorkAsync(Guid workId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, string>> GetBestUniverseCandidateQidsAsync(
        IReadOnlyCollection<Guid> workIds,
        CancellationToken ct = default);
    Task<IReadOnlyList<UnlinkedWorkDto>> GetUniverseUnlinkedAsync(CancellationToken ct = default);
}

public interface ILibraryOverviewReadService
{
    Task<LibraryOverviewReadModel> GetOverviewAggregatesAsync(CancellationToken ct);
}

public interface ILibraryWorkFeedReadService
{
    Task<PagedResponse<LibraryWorkListItemDto>> GetWorksAsync(
        PagedRequest page,
        CancellationToken ct = default);
}

public interface IWorkDetailReadService
{
    Task<WorkDetailDto?> GetAsync(Guid workId, CancellationToken ct = default);
}
