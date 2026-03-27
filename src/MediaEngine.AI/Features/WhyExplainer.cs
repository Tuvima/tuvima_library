using MediaEngine.AI.Llama;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using Microsoft.Extensions.Logging;

namespace MediaEngine.AI.Features;

public sealed class WhyExplainer : IWhyExplainer
{
    private readonly ILlamaInferenceService _llama;
    private readonly ICanonicalValueRepository _canonicalRepo;
    private readonly ITasteProfiler _tasteProfiler;
    private readonly ILogger<WhyExplainer> _logger;

    public WhyExplainer(
        ILlamaInferenceService llama,
        ICanonicalValueRepository canonicalRepo,
        ITasteProfiler tasteProfiler,
        ILogger<WhyExplainer> logger)
    {
        _llama          = llama;
        _canonicalRepo  = canonicalRepo;
        _tasteProfiler  = tasteProfiler;
        _logger         = logger;
    }

    public async Task<string?> ExplainAsync(Guid userId, Guid workId, CancellationToken ct = default)
    {
        _logger.LogDebug("WhyExplainer.ExplainAsync: generating explanation for user {User}, work {Work}",
            userId, workId);

        // 1. Load the user's taste profile.
        TasteProfile profile;
        try
        {
            profile = await _tasteProfiler.GetProfileAsync(userId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WhyExplainer: failed to load taste profile — skipping explanation");
            return null;
        }

        // 2. Load the work's canonical values.
        var canonicals = await _canonicalRepo.GetByEntityAsync(workId, ct).ConfigureAwait(false);
        if (canonicals.Count == 0)
        {
            _logger.LogDebug("WhyExplainer: no canonical values for work {Work}", workId);
            return null;
        }

        var lookup = canonicals.ToDictionary(c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase);

        var title       = lookup.GetValueOrDefault("title") ?? workId.ToString();
        var genre       = lookup.GetValueOrDefault("genre");
        var description = lookup.GetValueOrDefault("description");
        var vibe        = lookup.GetValueOrDefault("vibe");
        var mediaType   = lookup.GetValueOrDefault("media_type");

        // 3. Build a prompt comparing the user's profile to the work's attributes.
        var topGenres = string.Join(", ",
            profile.GenreDistribution.OrderByDescending(kv => kv.Value).Take(3).Select(kv => kv.Key));
        var topMoods = string.Join(", ",
            profile.MoodPreferences.OrderByDescending(kv => kv.Value).Take(2).Select(kv => kv.Key));

        var prompt = $"""
            You explain in 1-2 sentences why a specific work matches a user's taste.
            Be specific, warm, and concise. Do not use bullet points or headers.

            User's profile:
            - Favorite genres: {(string.IsNullOrEmpty(topGenres) ? "mixed" : topGenres)}
            - Preferred moods: {(string.IsNullOrEmpty(topMoods) ? "mixed" : topMoods)}
            - Profile summary: {profile.Summary ?? "varied tastes"}

            Work:
            - Title: {title}
            - Type: {mediaType ?? "unknown"}
            - Genre: {genre ?? "unknown"}
            - Mood/vibe: {vibe ?? "unknown"}
            - Description: {(string.IsNullOrEmpty(description) ? "not available" : description[..Math.Min(200, description.Length)])}

            Why this work matches the user's taste:
            """;

        // 4. Call the LLM with text_fast for a 1-2 sentence explanation.
        try
        {
            var explanation = await _llama.InferAsync(AiModelRole.TextFast, prompt, ct: ct).ConfigureAwait(false);
            return explanation?.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WhyExplainer: LLM call failed for work {Work}", workId);
            return null;
        }
    }
}
