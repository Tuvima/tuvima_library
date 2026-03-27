using MediaEngine.AI.Configuration;
using MediaEngine.AI.Llama;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using Microsoft.Extensions.Logging;

namespace MediaEngine.AI.Features;

/// <summary>
/// AI-powered media type classification. Replaces heuristic disambiguation
/// in AudioProcessor and VideoProcessor.
/// </summary>
public sealed class MediaTypeAdvisor : IMediaTypeAdvisor
{
    private readonly ILlamaInferenceService _llama;
    private readonly AiSettings _settings;
    private readonly ILogger<MediaTypeAdvisor> _logger;

    public MediaTypeAdvisor(
        ILlamaInferenceService llama,
        AiSettings settings,
        ILogger<MediaTypeAdvisor> logger)
    {
        _llama = llama;
        _settings = settings;
        _logger = logger;
    }

    public async Task<MediaTypeCandidate> ClassifyAsync(
        string filename,
        string? container,
        double? durationSeconds,
        int? bitrate,
        string? genre,
        bool hasChapters,
        string? folderPath,
        CancellationToken ct = default)
    {
        try
        {
            var prompt = PromptTemplates.MediaTypePrompt(
                filename, container, durationSeconds, bitrate, genre, hasChapters, folderPath);

            var result = await _llama.InferJsonAsync<MediaTypeResponse>(
                AiModelRole.TextQuality,
                prompt,
                PromptTemplates.MediaTypeGrammar,
                ct);

            if (result is null || string.IsNullOrWhiteSpace(result.Type))
            {
                _logger.LogWarning("MediaTypeAdvisor returned null for: {Filename}", filename);
                return new MediaTypeCandidate
                {
                    Type = MediaType.Unknown,
                    Confidence = 0.1,
                    Reason = "AI classification returned empty result",
                };
            }

            var mediaType = ParseMediaType(result.Type);
            var confidence = Math.Clamp(result.Confidence, 0.0, 1.0);

            _logger.LogInformation(
                "MediaTypeAdvisor: {Filename} → {Type} ({Conf:F2}) — {Reason}",
                filename, mediaType, confidence, result.Reason ?? "no reason");

            return new MediaTypeCandidate
            {
                Type = mediaType,
                Confidence = confidence,
                Reason = result.Reason ?? $"AI classified as {mediaType}",
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MediaTypeAdvisor failed for: {Filename}", filename);
            return new MediaTypeCandidate
            {
                Type = MediaType.Unknown,
                Confidence = 0.1,
                Reason = $"AI classification error: {ex.Message}",
            };
        }
    }

    private static MediaType ParseMediaType(string type) => type.Trim().ToLowerInvariant() switch
    {
        "book" or "books" => MediaType.Books,
        "audiobook" or "audiobooks" => MediaType.Audiobooks,
        "movie" or "movies" => MediaType.Movies,
        "tv" => MediaType.TV,
        "music" => MediaType.Music,
        "comic" or "comics" => MediaType.Comics,
        "podcast" or "podcasts" => MediaType.Podcasts,
        _ => MediaType.Unknown,
    };

    /// <summary>Internal DTO matching the GBNF grammar output.</summary>
    private sealed class MediaTypeResponse
    {
        public string? Type { get; set; }
        public double Confidence { get; set; }
        public string? Reason { get; set; }
    }
}
