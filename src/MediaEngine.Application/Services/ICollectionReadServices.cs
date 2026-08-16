using MediaEngine.Application.ReadModels;
using MediaEngine.Contracts.Collections;
using MediaEngine.Contracts.Search;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Models;

namespace MediaEngine.Application.Services;

public interface ICollectionBrowseReadService
{
    Task<List<CollectionDto>> GetAllAsync(CancellationToken ct);
    Task<Guid?> GetRootWorkIdAsync(Guid workId, CancellationToken ct);
    Task<Guid?> GetRepresentativeAssetIdAsync(Guid workId, CancellationToken ct);
    Task<Dictionary<Guid, Guid?>> GetPrimaryAssetIdsAsync(IEnumerable<Guid> workIds, CancellationToken ct);
    Task<CollectionPaletteReadModel?> GetAssetPaletteAsync(Guid entityId, CancellationToken ct);
    Task<IReadOnlyList<CollectionArtistWorkReadModel>> GetArtistWorksAsync(string artistName, CancellationToken ct);
    Task<IReadOnlyList<CollectionSystemViewDetailWorkReadModel>> GetSystemViewDetailWorksAsync(
        string groupField,
        string groupValue,
        string? mediaType,
        string? artistName,
        CancellationToken ct);
    IReadOnlyList<Guid> EvaluateRules(
        CollectionRuleDefinition definition,
        string? sortField = null,
        string sortDirection = "desc",
        int limit = 0,
        string? query = null,
        string? secondarySortField = null,
        string? secondarySortDirection = null);
    int CountRuleMatches(CollectionRuleDefinition definition, string? query = null);
    Task<IReadOnlyList<string>> GetFieldValuesAsync(string field, string? query, int limit, CancellationToken ct);
    Task<IReadOnlyList<CollectionRuleValueDto>> GetEntityFieldValuesAsync(string field, string? query, int limit, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<CollectionRuleValueDto>>([]);
    Task<List<ContentGroupDto>> GetSystemViewGroupsAsync(string? mediaType, string? groupField, CancellationToken ct);
}

public interface ICollectionMediaLookupReadService
{
    Task<List<CollectionMediaLookupDto>> LookupAsync(
        string? query,
        string? mediaTypes,
        IReadOnlySet<Guid> existingWorkIds,
        int? offset,
        int? limit,
        CancellationToken ct);

    Task<List<CollectionItemDto>> ResolveItemsAsync(
        Guid collectionId,
        IReadOnlyList<CollectionItem> items,
        CancellationToken ct);

    Task<List<CollectionResolvedItemDto>> ResolveMetadataAsync(
        IReadOnlyList<Guid> workIds,
        CancellationToken ct);

    Task<Dictionary<string, int>> CountMediaTypesAsync(
        IReadOnlyList<Guid> workIds,
        CancellationToken ct);
}

public interface ICollectionSearchReadService
{
    Task<List<SearchResultDto>> SearchAsync(string? query, CancellationToken ct);
}
