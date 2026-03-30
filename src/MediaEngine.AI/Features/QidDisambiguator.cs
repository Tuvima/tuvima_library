using MediaEngine.AI.Llama;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using Microsoft.Extensions.Logging;

namespace MediaEngine.AI.Features;

public sealed class QidDisambiguator : IQidDisambiguator
{
    private readonly ILlamaInferenceService _llama;
    private readonly ILogger<QidDisambiguator> _logger;

    public QidDisambiguator(ILlamaInferenceService llama, ILogger<QidDisambiguator> logger)
    {
        _llama = llama;
        _logger = logger;
    }

    public async Task<DisambiguationResult> DisambiguateAsync(
        IReadOnlyDictionary<string, string> fileMetadata,
        IReadOnlyList<QidCandidate> candidates,
        CancellationToken ct = default)
    {
        if (candidates.Count == 0)
            return new DisambiguationResult { Confidence = 0 };

        if (candidates.Count == 1)
            return new DisambiguationResult
            {
                SelectedQid = candidates[0].Qid,
                Confidence = ClaimConfidence.QidDisambiguator,
                Reasoning = "Single candidate — auto-selected",
            };

        var prompt = PromptTemplates.QidDisambiguationPrompt(fileMetadata, candidates);

        var result = await _llama.InferJsonAsync<QidDisambiguationResponse>(
            AiModelRole.TextQuality,
            prompt,
            PromptTemplates.QidDisambiguationGrammar,
            ct);

        if (result is null || string.IsNullOrWhiteSpace(result.SelectedQid))
        {
            _logger.LogWarning("QidDisambiguator could not select from {Count} candidates", candidates.Count);
            return new DisambiguationResult { Confidence = 0 };
        }

        // Validate that the selected QID is actually in the candidate list.
        var match = candidates.FirstOrDefault(c =>
            string.Equals(c.Qid, result.SelectedQid, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            _logger.LogWarning("QidDisambiguator selected {Qid} which is not in the candidate list", result.SelectedQid);
            return new DisambiguationResult { Confidence = 0 };
        }

        var confidence = Math.Clamp(result.Confidence, 0.0, 1.0);
        _logger.LogInformation(
            "QidDisambiguator selected {Qid} ({Label}) with confidence {Conf:F2}: {Reason}",
            match.Qid, match.Label, confidence, result.Reasoning);

        return new DisambiguationResult
        {
            SelectedQid = match.Qid,
            Confidence = confidence,
            Reasoning = result.Reasoning,
        };
    }

    private sealed class QidDisambiguationResponse
    {
        public string? SelectedQid { get; set; }
        public double Confidence { get; set; }
        public string? Reasoning { get; set; }
    }
}
