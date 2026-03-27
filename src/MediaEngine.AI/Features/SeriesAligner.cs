using MediaEngine.AI.Llama;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MediaEngine.AI.Features;

public sealed class SeriesAligner : ISeriesAligner
{
    private readonly ILlamaInferenceService _llama;
    private readonly ILogger<SeriesAligner> _logger;

    public SeriesAligner(ILlamaInferenceService llama, ILogger<SeriesAligner> logger)
    {
        _llama = llama;
        _logger = logger;
    }

    public async Task<int?> InferPositionAsync(
        string workTitle,
        string seriesName,
        IReadOnlyList<string> siblingTitles,
        CancellationToken ct = default)
    {
        var prompt = PromptTemplates.SeriesPositionPrompt(workTitle, seriesName, siblingTitles);

        var result = await _llama.InferJsonAsync<SeriesPositionResponse>(
            AiModelRole.TextQuality,
            prompt,
            PromptTemplates.SeriesPositionGrammar,
            ct);

        if (result is null || !result.Position.HasValue || result.Position < 1)
        {
            _logger.LogDebug("SeriesAligner could not determine position for \"{Title}\" in \"{Series}\"",
                workTitle, seriesName);
            return null;
        }

        _logger.LogInformation(
            "SeriesAligner: \"{Title}\" is position {Pos} in \"{Series}\" (confidence: {Conf:F2})",
            workTitle, result.Position, seriesName, result.Confidence);

        return result.Position;
    }

    public Task<IReadOnlyList<(Guid WorkId, Guid SuggestedHubId)>> DetectUngroupedAsync(
        CancellationToken ct = default)
    {
        // Implemented in SeriesAlignmentBackgroundService which queries the DB
        // and calls InferPositionAsync per work. This method is a placeholder
        // for future direct detection using LLM analysis of the full work list.
        return Task.FromResult<IReadOnlyList<(Guid, Guid)>>(Array.Empty<(Guid, Guid)>());
    }

    private sealed class SeriesPositionResponse
    {
        public int? Position { get; set; }
        public double Confidence { get; set; }
    }
}
