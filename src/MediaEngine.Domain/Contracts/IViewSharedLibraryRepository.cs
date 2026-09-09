using MediaEngine.Domain.PersonalMedia;

namespace MediaEngine.Domain.Contracts;

public interface IViewSharedLibraryRepository
{
    Task<ViewSharedLibrary> GetAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ViewSharedSource>> GetSourcesAsync(CancellationToken ct = default);
    Task<ViewSharedSource> UpsertSourceAsync(ViewSharedSource source, CancellationToken ct = default);
    Task<bool> DeleteSourceAsync(Guid sourceId, CancellationToken ct = default);
}
