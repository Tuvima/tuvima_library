using MediaEngine.AI.Llama;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using Microsoft.Extensions.Logging;

namespace MediaEngine.AI.Features;

public sealed class TasteProfiler : ITasteProfiler
{
    private readonly ILlamaInferenceService _llama;
    private readonly ICanonicalValueRepository _canonicalRepo;
    private readonly ILogger<TasteProfiler> _logger;

    public TasteProfiler(
        ILlamaInferenceService llama,
        ICanonicalValueRepository canonicalRepo,
        ILogger<TasteProfiler> logger)
    {
        _llama = llama;
        _canonicalRepo = canonicalRepo;
        _logger = logger;
    }

    public async Task<TasteProfile> GetProfileAsync(Guid userId, CancellationToken ct = default)
    {
        _logger.LogDebug("TasteProfiler.GetProfileAsync: building profile for user {User}", userId);

        // Build distributions from all canonical values across the library.
        // (User-scoping will be added when user_taste_profiles migration M-058 is in place.)
        var genreCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var eraCounts   = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var typeCounts  = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var moodCounts  = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Walk every entity that has a genre canonical value to build distributions.
        // This is a heuristic scan — we use FindByValueAsync with empty prefix to get all.
        var allGenreOwners = await _canonicalRepo.FindByKeyAndPrefixAsync("genre", "", ct).ConfigureAwait(false);
        foreach (var cv in allGenreOwners)
        {
            if (!string.IsNullOrWhiteSpace(cv.Value))
                IncrementKey(genreCounts, cv.Value.Trim().ToLowerInvariant());
        }

        var allYearOwners = await _canonicalRepo.FindByKeyAndPrefixAsync("release_year", "", ct).ConfigureAwait(false);
        foreach (var cv in allYearOwners)
        {
            if (int.TryParse(cv.Value, out var year))
            {
                var decade = $"{(year / 10) * 10}s";
                IncrementKey(eraCounts, decade);
            }
        }

        var allTypeOwners = await _canonicalRepo.FindByKeyAndPrefixAsync("media_type", "", ct).ConfigureAwait(false);
        foreach (var cv in allTypeOwners)
        {
            if (!string.IsNullOrWhiteSpace(cv.Value))
                IncrementKey(typeCounts, cv.Value.Trim());
        }

        var allVibeOwners = await _canonicalRepo.FindByKeyAndPrefixAsync("vibe", "", ct).ConfigureAwait(false);
        foreach (var cv in allVibeOwners)
        {
            if (!string.IsNullOrWhiteSpace(cv.Value))
                IncrementKey(moodCounts, cv.Value.Trim().ToLowerInvariant());
        }

        var genreDist = ToDistribution(genreCounts);
        var eraDist   = ToDistribution(eraCounts);
        var typeDist  = ToDistribution(typeCounts);
        var moodDist  = ToDistribution(moodCounts);

        // Generate a human-readable summary using the LLM.
        string? summary = null;
        if (genreDist.Count > 0 || typeDist.Count > 0)
        {
            try
            {
                var topGenres  = string.Join(", ", genreDist.OrderByDescending(kv => kv.Value).Take(3).Select(kv => kv.Key));
                var topTypes   = string.Join(", ", typeDist.OrderByDescending(kv => kv.Value).Take(3).Select(kv => kv.Key));
                var topEras    = string.Join(", ", eraDist.OrderByDescending(kv => kv.Value).Take(2).Select(kv => kv.Key));
                var prompt     = $"""
                    Summarize this reader/viewer's taste profile in 1-2 sentences.
                    Favorite genres: {(string.IsNullOrEmpty(topGenres) ? "mixed" : topGenres)}
                    Preferred media types: {(string.IsNullOrEmpty(topTypes) ? "mixed" : topTypes)}
                    Favorite eras: {(string.IsNullOrEmpty(topEras) ? "mixed" : topEras)}
                    Be concise and friendly. Do not use bullet points.
                    """;

                summary = await _llama.InferAsync(AiModelRole.TextFast, prompt, ct: ct).ConfigureAwait(false);
                summary = summary?.Trim();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TasteProfiler: LLM summary generation failed — using fallback");
                summary = "Profile generated from library patterns.";
            }
        }

        return new TasteProfile
        {
            UserId            = userId,
            GenreDistribution = genreDist,
            EraPreferences    = eraDist,
            MediaTypeMix      = typeDist,
            MoodPreferences   = moodDist,
            Summary           = summary ?? "Profile not yet generated — add more items to your library.",
            LastUpdatedAt     = DateTimeOffset.UtcNow,
        };
    }

    public Task UpdateAsync(Guid userId, Guid assetId, CancellationToken ct = default)
    {
        _logger.LogDebug("TasteProfiler.UpdateAsync: incremental update for user {User}, asset {Asset}", userId, assetId);
        // Full incremental update will be implemented when UserState tracking (§3.15) is in place.
        return Task.CompletedTask;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void IncrementKey(Dictionary<string, int> dict, string key)
    {
        dict[key] = dict.TryGetValue(key, out var v) ? v + 1 : 1;
    }

    private static IReadOnlyDictionary<string, double> ToDistribution(Dictionary<string, int> counts)
    {
        if (counts.Count == 0) return new Dictionary<string, double>();
        var total = (double)counts.Values.Sum();
        return counts.ToDictionary(
            kv => kv.Key,
            kv => Math.Round(kv.Value / total, 2));
    }
}
