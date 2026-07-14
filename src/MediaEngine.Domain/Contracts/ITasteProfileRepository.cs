using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Contracts;

public interface ITasteProfileRepository
{
    Task<TasteProfile?> GetAsync(Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<TasteSignal>> GetSignalsAsync(
        Guid userId,
        int limit,
        CancellationToken ct = default);

    Task<AiFeatureWriteResult> ReplaceAiProfileAsync(
        TasteProfilePersistenceRequest request,
        CancellationToken ct = default);
}
