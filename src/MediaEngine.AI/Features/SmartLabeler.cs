using MediaEngine.AI.Configuration;
using MediaEngine.AI.Llama;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using Microsoft.Extensions.Logging;

namespace MediaEngine.AI.Features;

/// <summary>
/// AI-powered filename cleaning. Replaces TitleNormalizer.
/// Uses the text_quality model with GBNF grammar constraints.
/// </summary>
public sealed class SmartLabeler : ISmartLabeler
{
    private readonly ILlamaInferenceService _llama;
    private readonly AiSettings _settings;
    private readonly ILogger<SmartLabeler> _logger;

    public SmartLabeler(
        ILlamaInferenceService llama,
        AiSettings settings,
        ILogger<SmartLabeler> logger)
    {
        _llama = llama;
        _settings = settings;
        _logger = logger;
    }

    public async Task<CleanedSearchQuery> CleanAsync(string rawFilename, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawFilename))
            return new CleanedSearchQuery { Title = string.Empty, Confidence = 0 };

        // Strip extension if present.
        var stem = Path.GetFileNameWithoutExtension(rawFilename);
        if (string.IsNullOrWhiteSpace(stem))
            stem = rawFilename;

        try
        {
            var prompt = PromptTemplates.SmartLabelingPrompt(stem);
            var result = await _llama.InferJsonAsync<SmartLabelingResponse>(
                AiModelRole.TextQuality,
                prompt,
                PromptTemplates.SmartLabelingGrammar,
                ct);

            if (result is null || string.IsNullOrWhiteSpace(result.Title))
            {
                _logger.LogWarning("SmartLabeler returned null/empty for: {Filename}", stem);
                return new CleanedSearchQuery
                {
                    Title = stem,
                    Confidence = 0.1,
                };
            }

            // Validate year range.
            int? year = result.Year;
            if (year.HasValue && (year < 1800 || year > 2100))
                year = null;

            // Validate season/episode.
            int? season = result.Season;
            int? episode = result.Episode;
            if (season.HasValue && (season < 0 || season > 100)) season = null;
            if (episode.HasValue && (episode < 0 || episode > 9999)) episode = null;

            // Clamp confidence.
            var confidence = Math.Clamp(result.Confidence, 0.0, 1.0);

            _logger.LogInformation(
                "SmartLabeler: \"{Raw}\" → \"{Title}\" (author: {Author}, year: {Year}, confidence: {Conf:F2})",
                stem, result.Title, result.Author ?? "none", year?.ToString() ?? "none", confidence);

            return new CleanedSearchQuery
            {
                Title = result.Title.Trim(),
                Author = string.IsNullOrWhiteSpace(result.Author) ? null : result.Author.Trim(),
                Year = year,
                Season = season,
                Episode = episode,
                Confidence = confidence,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SmartLabeler failed for: {Filename}", stem);
            return new CleanedSearchQuery
            {
                Title = stem,
                Confidence = 0.1,
            };
        }
    }

    /// <summary>Internal DTO matching the GBNF grammar output.</summary>
    private sealed class SmartLabelingResponse
    {
        public string? Title { get; set; }
        public string? Author { get; set; }
        public int? Year { get; set; }
        public int? Season { get; set; }
        public int? Episode { get; set; }
        public double Confidence { get; set; }
    }
}
