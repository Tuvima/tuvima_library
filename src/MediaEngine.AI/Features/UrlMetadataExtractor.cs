using MediaEngine.AI.Llama;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MediaEngine.AI.Features;

public sealed class UrlMetadataExtractor : IUrlMetadataExtractor
{
    private readonly LlamaInferenceService _llama;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<UrlMetadataExtractor> _logger;

    public UrlMetadataExtractor(
        LlamaInferenceService llama,
        IHttpClientFactory httpClientFactory,
        ILogger<UrlMetadataExtractor> logger)
    {
        _llama = llama;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<UrlExtractionResult> ExtractAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return new UrlExtractionResult { Success = false, ErrorMessage = "URL is empty" };

        try
        {
            // Fetch the page content.
            var client = _httpClientFactory.CreateClient("UrlExtractor");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TuvimaLibrary/1.0");
            client.Timeout = TimeSpan.FromSeconds(15);

            var html = await client.GetStringAsync(url, ct);

            // Truncate to avoid overwhelming the LLM.
            if (html.Length > 10000)
                html = html[..10000];

            var prompt = $"""
                Extract structured metadata from this web page HTML.
                Look for: title, author, year, isbn, description, publisher, series, genre.
                Return ONLY valid JSON with field names as keys and extracted values.

                URL: {url}
                HTML content:
                {html}
                """;

            var grammar = """
                root    ::= "{" ws (pair ("," ws pair)*)? ws "}"
                pair    ::= string ws ":" ws value
                value   ::= string | "null"
                string  ::= "\"" ([^"\\] | "\\" .)* "\""
                ws      ::= [ \t\n]*
                """;

            var result = await _llama.InferJsonAsync<Dictionary<string, string?>>(
                AiModelRole.TextQuality, prompt, grammar, ct);

            if (result is null || result.Count == 0)
            {
                _logger.LogWarning("UrlMetadataExtractor returned empty for: {Url}", url);
                return new UrlExtractionResult { Success = false, ErrorMessage = "No metadata could be extracted" };
            }

            var fields = result
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value!);

            _logger.LogInformation("UrlMetadataExtractor: {Url} → {Count} fields extracted", url, fields.Count);

            return new UrlExtractionResult
            {
                Success = true,
                Fields = fields,
                Confidence = 0.75,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UrlMetadataExtractor failed for: {Url}", url);
            return new UrlExtractionResult { Success = false, ErrorMessage = ex.Message };
        }
    }
}
