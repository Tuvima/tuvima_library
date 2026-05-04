using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Contracts;

public interface ISeriesManifestRepository
{
    Task<SeriesManifestHydration?> GetHydrationAsync(string seriesQid, CancellationToken ct = default);

    Task<IReadOnlyList<SeriesManifestItemRecord>> GetItemsBySeriesQidAsync(
        string seriesQid,
        CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, IReadOnlyList<Guid>>> FindWorkIdsByQidsAsync(
        IReadOnlyCollection<string> qids,
        CancellationToken ct = default);

    Task UpsertManifestAsync(
        SeriesManifestHydration hydration,
        IReadOnlyList<SeriesManifestItemRecord> items,
        CancellationToken ct = default);

    Task LinkOwnedWorksAsync(
        Guid collectionId,
        IReadOnlyList<SeriesManifestItemRecord> items,
        CancellationToken ct = default);

    Task<SeriesManifestViewDto?> GetViewByCollectionIdAsync(
        Guid collectionId,
        CancellationToken ct = default);
}
