using MediaEngine.AI.Configuration;
using MediaEngine.AI.Llama;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using Microsoft.Extensions.Logging;

namespace MediaEngine.AI.Features;

/// <summary>
/// LLM-powered description analysis. Gathers all available descriptions for an entity,
/// runs a single GBNF-constrained inference, and returns structured intelligence:
/// people, themes, mood, setting, time period, audience, content warnings, pace, TL;DR.
/// </summary>
public sealed class DescriptionIntelligenceService : IDescriptionIntelligenceService
{
    private readonly LlamaInferenceService _llama;
    private readonly AiSettings _settings;
    private readonly ICanonicalValueRepository _canonicalRepo;
    private readonly ILogger<DescriptionIntelligenceService> _logger;

    public DescriptionIntelligenceService(
        LlamaInferenceService llama,
        AiSettings settings,
        ICanonicalValueRepository canonicalRepo,
        ILogger<DescriptionIntelligenceService> logger)
    {
        _llama = llama;
        _settings = settings;
        _canonicalRepo = canonicalRepo;
        _logger = logger;
    }

    public async Task<DescriptionIntelligenceResult?> AnalyzeAsync(
        Guid entityId,
        string mediaCategory,
        CancellationToken ct = default)
    {
        if (!_settings.Features.DescriptionIntelligence)
        {
            _logger.LogDebug("[DESCRIPTION-INTEL] Feature disabled — skipping for {Id}", entityId);
            return null;
        }

        try
        {
            // Gather all available descriptions from canonical values.
            var canonicals = await _canonicalRepo.GetByEntityAsync(entityId, ct).ConfigureAwait(false);
            if (canonicals is null || canonicals.Count == 0)
                return null;

            var descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cv in canonicals)
            {
                if (string.Equals(cv.Key, "plot_summary", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(cv.Value))
                    descriptions["Wikipedia Plot"] = cv.Value;
                else if (string.Equals(cv.Key, "description", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(cv.Value))
                    descriptions["Description"] = cv.Value;
            }

            if (descriptions.Count == 0)
            {
                _logger.LogDebug("[DESCRIPTION-INTEL] No descriptions available for {Id}", entityId);
                return null;
            }

            // Build combined description text with source labels.
            // Priority: Wikipedia plot (richest) → Description (Apple API/Wikipedia extract)
            // Truncate per source, cap total at 3000 chars.
            var parts = new List<string>();
            int remaining = 3000;

            foreach (var (label, text) in descriptions.OrderByDescending(d =>
                d.Key.Contains("Plot", StringComparison.OrdinalIgnoreCase) ? 2 : 1))
            {
                var maxLen = label.Contains("Plot", StringComparison.OrdinalIgnoreCase) ? 1500 : 800;
                var truncated = text.Length > maxLen ? text[..maxLen] + "..." : text;
                if (truncated.Length > remaining) truncated = truncated[..remaining];
                if (truncated.Length > 0)
                {
                    parts.Add($"[{label}]: {truncated}");
                    remaining -= truncated.Length;
                }
                if (remaining <= 0) break;
            }

            var combinedDescriptions = string.Join("\n\n", parts);

            // Get title from canonicals.
            var title = canonicals.FirstOrDefault(c =>
                string.Equals(c.Key, "title", StringComparison.OrdinalIgnoreCase))?.Value ?? "Unknown";

            // Get mood vocabulary for this media category.
            var moodVocab = _settings.VibeVocabulary.GetForCategory(mediaCategory);

            // Build prompt and run inference.
            var prompt = PromptTemplates.DescriptionIntelligencePrompt(
                title, mediaCategory, moodVocab, combinedDescriptions);

            _logger.LogInformation(
                "[DESCRIPTION-INTEL] Analyzing {Title} ({Category}) — {DescLen} chars of descriptions",
                title, mediaCategory, combinedDescriptions.Length);

            var result = await _llama.InferJsonAsync<DescriptionIntelligenceResponse>(
                AiModelRole.TextQuality,
                prompt,
                PromptTemplates.DescriptionIntelligenceGrammar,
                ct);

            if (result is null)
            {
                _logger.LogWarning("[DESCRIPTION-INTEL] LLM returned null for {Title}", title);
                return null;
            }

            // Validate and filter results.
            var validMood = (result.Mood ?? [])
                .Where(m => moodVocab.Contains(m, StringComparer.OrdinalIgnoreCase))
                .Take(3)
                .ToList();

            var validPeople = (result.People ?? [])
                .Where(p => !string.IsNullOrWhiteSpace(p.Name) && p.Name.Contains(' '))
                .Select(p => new ExtractedPersonRef(
                    p.Name.Trim(),
                    NormalizeRole(p.Role),
                    Math.Clamp(p.Confidence, 0.0, 1.0)))
                .ToList();

            var intelligence = new DescriptionIntelligenceResult
            {
                People = validPeople,
                Themes = (result.Themes ?? []).Take(5).ToList(),
                Mood = validMood,
                Setting = string.IsNullOrWhiteSpace(result.Setting) ? null : result.Setting.Trim(),
                TimePeriod = string.IsNullOrWhiteSpace(result.TimePeriod) ? null : result.TimePeriod.Trim(),
                Audience = string.IsNullOrWhiteSpace(result.Audience) ? null : result.Audience.Trim(),
                ContentWarnings = (result.ContentWarnings ?? []).Take(5).ToList(),
                Pace = string.IsNullOrWhiteSpace(result.Pace) ? null : result.Pace.Trim(),
                Tldr = string.IsNullOrWhiteSpace(result.Tldr) ? null : result.Tldr.Trim(),
            };

            _logger.LogInformation(
                "[DESCRIPTION-INTEL] {Title}: {People} people, {Themes} themes, {Mood} mood, setting={Setting}, tldr={HasTldr}",
                title, intelligence.People.Count, intelligence.Themes.Count, intelligence.Mood.Count,
                intelligence.Setting ?? "none", intelligence.Tldr is not null);

            return intelligence;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DESCRIPTION-INTEL] Failed for entity {Id}", entityId);
            return null;
        }
    }

    private static string NormalizeRole(string? role) => (role?.Trim().ToLowerInvariant()) switch
    {
        "narrator"   => "Narrator",
        "translator" => "Translator",
        "editor"     => "Editor",
        "illustrator"=> "Illustrator",
        "director"   => "Director",
        "cast"       => "Cast Member",
        "host"       => "Host",
        "producer"   => "Producer",
        "author"     => "Author",
        _            => "Author",
    };

    /// <summary>Internal DTO matching the GBNF grammar output.</summary>
    private sealed class DescriptionIntelligenceResponse
    {
        public List<PersonDto>? People { get; set; }
        public List<string>? Themes { get; set; }
        public List<string>? Mood { get; set; }
        public string? Setting { get; set; }
        public string? TimePeriod { get; set; }
        public string? Audience { get; set; }
        public List<string>? ContentWarnings { get; set; }
        public string? Pace { get; set; }
        public string? Tldr { get; set; }
    }

    private sealed class PersonDto
    {
        public string Name { get; set; } = "";
        public string? Role { get; set; }
        public double Confidence { get; set; }
    }
}
