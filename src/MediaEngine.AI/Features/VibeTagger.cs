using MediaEngine.AI.Configuration;
using MediaEngine.AI.Llama;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MediaEngine.AI.Features;

public sealed class VibeTagger : IVibeTagger
{
    private readonly LlamaInferenceService _llama;
    private readonly AiSettings _settings;
    private readonly ILogger<VibeTagger> _logger;

    public VibeTagger(LlamaInferenceService llama, AiSettings settings, ILogger<VibeTagger> logger)
    {
        _llama = llama;
        _settings = settings;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> TagAsync(
        string title,
        string? wikipediaSummary,
        IReadOnlyList<string> genres,
        string mediaCategory,
        CancellationToken ct = default)
    {
        var vocabulary = _settings.VibeVocabulary.GetForCategory(mediaCategory);
        if (vocabulary.Count == 0 || string.IsNullOrWhiteSpace(wikipediaSummary))
            return [];

        var vocabList = string.Join(", ", vocabulary);
        var genreList = genres.Count > 0 ? string.Join(", ", genres) : "unknown";

        var prompt = $"""
            You select mood/vibe tags for media from a controlled vocabulary.
            Select 3-5 tags that best describe this work's mood and feel.
            Base your analysis on the Wikipedia summary content, NOT on who created it.
            Return ONLY a JSON array of tag strings from the vocabulary.

            Vocabulary: [{vocabList}]
            Title: {title}
            Genres: {genreList}
            Category: {mediaCategory}

            Wikipedia summary:
            {wikipediaSummary[..Math.Min(wikipediaSummary.Length, 1500)]}
            """;

        var grammar = """
            root   ::= "[" ws (string ("," ws string)*)? ws "]"
            string ::= "\"" ([^"\\] | "\\" .)* "\""
            ws     ::= [ \t\n]*
            """;

        var result = await _llama.InferJsonAsync<List<string>>(
            AiModelRole.TextQuality, prompt, grammar, ct);

        if (result is null || result.Count == 0)
        {
            _logger.LogDebug("VibeTagger returned empty for \"{Title}\"", title);
            return [];
        }

        // Filter to only tags in the vocabulary.
        var validTags = result
            .Where(t => vocabulary.Contains(t, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

        _logger.LogInformation("VibeTagger: \"{Title}\" → [{Tags}]", title, string.Join(", ", validTags));
        return validTags;
    }
}
