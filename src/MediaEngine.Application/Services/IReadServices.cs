using MediaEngine.Application.ReadModels;

namespace MediaEngine.Application.Services;

public interface IJourneyReadService
{
    Task<IReadOnlyList<JourneyItemResponse>> GetJourneyAsync(
        Guid userId,
        Guid? collectionId,
        int limit,
        CancellationToken ct);
}

public interface IIngestionBatchReadService
{
    Task<IReadOnlyList<IngestionBatchItemResponse>> GetItemsAsync(
        Guid batchId,
        int limit,
        CancellationToken ct);
}

public interface IPersonAliasReadService
{
    Task<PersonAliasResponse?> GetAliasesAsync(Guid personId, CancellationToken ct);
}

public interface IPersonPresenceReadService
{
    Task<IReadOnlyDictionary<string, Dictionary<string, int>>> GetPresenceAsync(
        IReadOnlyList<Guid> personIds,
        CancellationToken ct);
}

public interface IPersonWorksReadService
{
    Task<IReadOnlySet<Guid>> GetCollectionIdsForPersonAsync(Guid personId, CancellationToken ct);
}

public interface IPersonAssetScopeReadService
{
    Task<IReadOnlyList<PersonSummaryResponse>> GetByCollectionAsync(Guid collectionId, CancellationToken ct);
    Task<IReadOnlyList<PersonSummaryResponse>> GetByWorkAsync(Guid workId, CancellationToken ct);
}
