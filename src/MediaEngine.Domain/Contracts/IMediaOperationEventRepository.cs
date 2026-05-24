using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Contracts;

public interface IMediaOperationEventRepository
{
    Task AddAsync(MediaOperationEvent evt, CancellationToken ct = default);
    Task<IReadOnlyList<MediaOperationEvent>> GetByOperationAsync(Guid operationId, CancellationToken ct = default);
    Task<IReadOnlyList<MediaOperationEvent>> GetByEntityAsync(Guid entityId, int limit, CancellationToken ct = default);
}
