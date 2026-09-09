using MediaEngine.Domain.Models;
namespace MediaEngine.Domain.Contracts;

public interface IPersonalStatusRepository
{
    Task<PersonalStatusSnapshot> ReadAsync(Guid profileId, PersonalStatusTarget target, CancellationToken ct = default);
    Task<PersonalStatusResult> ExecuteAsync(Guid profileId, PersonalStatusTarget target, PersonalStatusCommand command,
        Guid commandId, string expectedRevision, CancellationToken ct = default);
    Task<PersonalStatusResult> UndoAsync(Guid profileId, Guid commandId, CancellationToken ct = default,
        IReadOnlySet<Guid>? authorizedAssetIds = null);
    Task<IReadOnlyList<PersonalStatusHistory>> HistoryAsync(Guid profileId, PersonalStatusTarget target, CancellationToken ct = default);
}
