using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Persists complete AI feature projections without allowing generated values
/// to silently replace manual or trusted provider values.
/// </summary>
public interface IAiFeaturePersistenceRepository
{
    Task<AiFeatureWriteResult> ReplaceAiFeatureAsync(
        AiFeatureWriteRequest request,
        CancellationToken ct = default);

    Task<AiFeatureState?> GetAiFeatureStateAsync(
        Guid entityId,
        string featureKey,
        CancellationToken ct = default);

    Task<AiFeatureState> RecordAiFeatureFailureAsync(
        AiFeatureFailureRequest request,
        CancellationToken ct = default);
}
