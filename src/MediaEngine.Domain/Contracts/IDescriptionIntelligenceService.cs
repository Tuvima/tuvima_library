namespace MediaEngine.Domain.Contracts;

/// <summary>
/// LLM-powered description analysis. Reads all available descriptions for an entity
/// and extracts structured intelligence: people (with roles), vocabulary (themes, mood,
/// setting), and a TL;DR summary. Runs as a single unified LLM pass after Stage 2.
/// </summary>
public interface IDescriptionIntelligenceService
{
    /// <summary>
    /// Analyze all available descriptions for an entity and extract structured intelligence.
    /// Returns null if the feature is disabled, no descriptions are available, or the LLM fails.
    /// </summary>
    Task<Domain.Models.DescriptionIntelligenceResult?> AnalyzeAsync(
        Guid entityId,
        string mediaCategory,
        CancellationToken ct = default);
}
