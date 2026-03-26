namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Generates human-readable explanations for why a work is recommended to a user.
/// </summary>
public interface IWhyExplainer
{
    /// <summary>
    /// Generate a 1-2 sentence explanation for a recommendation.
    /// Uses text_fast model for on-demand responsiveness.
    /// </summary>
    Task<string?> ExplainAsync(Guid userId, Guid workId, CancellationToken ct = default);
}
