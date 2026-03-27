using MediaEngine.AI.Llama;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Storage.Contracts;
using Microsoft.Extensions.Logging;

namespace MediaEngine.AI.Features;

public sealed class IntentSearchParser : IIntentSearchParser
{
    private readonly ILlamaInferenceService _llama;
    private readonly IConfigurationLoader _configLoader;
    private readonly ILogger<IntentSearchParser> _logger;

    public IntentSearchParser(
        ILlamaInferenceService llama,
        IConfigurationLoader configLoader,
        ILogger<IntentSearchParser> logger)
    {
        _llama = llama;
        _configLoader = configLoader;
        _logger = logger;
    }

    public async Task<IntentSearchResult> ParseAsync(string naturalLanguageQuery, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(naturalLanguageQuery))
            return new IntentSearchResult { OriginalQuery = naturalLanguageQuery ?? "", Confidence = 0 };

        var displayLang = _configLoader.LoadCore().Language.Display ?? "en";
        var langHint = !string.Equals(displayLang, "en", StringComparison.OrdinalIgnoreCase)
            ? $"The query may be in {displayLang}. Extract English keywords, genres, and moods regardless of input language.\n"
            : "";

        var prompt = $"""
            {langHint}Parse this natural language search query into structured filters.
            Extract: genres, moods/vibes, year range, media types, and keywords.
            Return ONLY valid JSON matching the grammar.

            Query: {naturalLanguageQuery}
            """;

        var grammar = """
            root   ::= "{" ws "\"genres\"" ws ":" ws arr ws "," ws "\"moods\"" ws ":" ws arr ws "," ws "\"year_from\"" ws ":" ws (number | "null") ws "," ws "\"year_to\"" ws ":" ws (number | "null") ws "," ws "\"media_types\"" ws ":" ws arr ws "," ws "\"keywords\"" ws ":" ws arr ws "," ws "\"confidence\"" ws ":" ws number ws "}"
            arr    ::= "[" ws (string ("," ws string)*)? ws "]"
            string ::= "\"" ([^"\\] | "\\" .)* "\""
            number ::= [0-9]+ ("." [0-9]+)?
            ws     ::= [ \t\n]*
            """;

        var result = await _llama.InferJsonAsync<IntentParseResponse>(
            AiModelRole.TextFast, prompt, grammar, ct);

        if (result is null)
        {
            _logger.LogDebug("IntentSearchParser returned null for: \"{Query}\"", naturalLanguageQuery);
            return new IntentSearchResult
            {
                OriginalQuery = naturalLanguageQuery,
                Keywords = [naturalLanguageQuery], // fallback to keyword search
                Confidence = 0.2,
            };
        }

        var mediaTypes = (result.MediaTypes ?? [])
            .Select(t => Enum.TryParse<MediaType>(t, true, out var mt) ? mt : (MediaType?)null)
            .Where(mt => mt.HasValue)
            .Select(mt => mt!.Value)
            .ToList();

        _logger.LogInformation(
            "IntentSearchParser: \"{Query}\" → genres:[{Genres}], moods:[{Moods}], years:{From}-{To}",
            naturalLanguageQuery,
            string.Join(",", result.Genres ?? []),
            string.Join(",", result.Moods ?? []),
            result.YearFrom?.ToString() ?? "?",
            result.YearTo?.ToString() ?? "?");

        return new IntentSearchResult
        {
            OriginalQuery = naturalLanguageQuery,
            Genres = result.Genres ?? [],
            Moods = result.Moods ?? [],
            YearFrom = result.YearFrom,
            YearTo = result.YearTo,
            MediaTypes = mediaTypes,
            Keywords = result.Keywords ?? [],
            Confidence = Math.Clamp(result.Confidence, 0.0, 1.0),
        };
    }

    private sealed class IntentParseResponse
    {
        public List<string>? Genres { get; set; }
        public List<string>? Moods { get; set; }
        public int? YearFrom { get; set; }
        public int? YearTo { get; set; }
        public List<string>? MediaTypes { get; set; }
        public List<string>? Keywords { get; set; }
        public double Confidence { get; set; }
    }
}
