using MediaEngine.AI.Llama;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MediaEngine.AI.Features;

public sealed class TldrGenerator : ITldrGenerator
{
    private readonly ILlamaInferenceService _llama;
    private readonly ILogger<TldrGenerator> _logger;

    public TldrGenerator(ILlamaInferenceService llama, ILogger<TldrGenerator> logger)
    {
        _llama = llama;
        _logger = logger;
    }

    public async Task<string?> SummarizeAsync(string longDescription, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(longDescription))
            return null;

        var prompt = $"""
            Condense this description into exactly ONE punchy sentence. No spoilers.
            Return ONLY valid JSON matching the grammar.

            Description:
            {longDescription[..Math.Min(longDescription.Length, 2000)]}
            """;

        var grammar = """
            root   ::= "{" ws "\"tldr\"" ws ":" ws string ws "}"
            string ::= "\"" ([^"\\] | "\\" .)* "\""
            ws     ::= [ \t\n]*
            """;

        var result = await _llama.InferJsonAsync<TldrResponse>(
            AiModelRole.TextFast, prompt, grammar, ct);

        if (result is null || string.IsNullOrWhiteSpace(result.Tldr))
        {
            _logger.LogDebug("TldrGenerator returned empty");
            return null;
        }

        _logger.LogInformation("TldrGenerator: {Length} chars → \"{Tldr}\"",
            longDescription.Length, result.Tldr);
        return result.Tldr;
    }

    private sealed class TldrResponse { public string? Tldr { get; set; } }
}
