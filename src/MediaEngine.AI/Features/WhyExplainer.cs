using MediaEngine.AI.Llama;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MediaEngine.AI.Features;

public sealed class WhyExplainer : IWhyExplainer
{
    private readonly LlamaInferenceService _llama;
    private readonly ILogger<WhyExplainer> _logger;

    public WhyExplainer(LlamaInferenceService llama, ILogger<WhyExplainer> logger)
    {
        _llama = llama;
        _logger = logger;
    }

    public async Task<string?> ExplainAsync(Guid userId, Guid workId, CancellationToken ct = default)
    {
        // Full implementation requires:
        // 1. Loading the user's TasteProfile
        // 2. Loading the work's canonical values (genre, vibe tags, etc.)
        // 3. Passing both to the LLM for explanation generation
        // Will be wired when TasteProfiler is fully operational.
        _logger.LogDebug("WhyExplainer.ExplainAsync: awaiting TasteProfiler integration for user {User}, work {Work}",
            userId, workId);
        return null;
    }
}
