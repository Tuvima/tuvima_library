using MediaEngine.Domain.Enums;

namespace MediaEngine.AI.Llama;

/// <summary>
/// Abstraction over LLamaSharp inference for structured JSON output.
/// Extracted to enable unit testing of AI feature services without loading model weights.
/// </summary>
public interface ILlamaInferenceService
{
    /// <summary>
    /// Run inference with a prompt and optional GBNF grammar constraint.
    /// Returns the raw text output from the LLM.
    /// </summary>
    Task<string> InferAsync(
        AiModelRole role,
        string prompt,
        string? gbnfGrammar = null,
        CancellationToken ct = default);

    /// <summary>
    /// Run inference and parse the result as JSON.
    /// Retries once with higher temperature if parsing fails.
    /// </summary>
    Task<T?> InferJsonAsync<T>(
        AiModelRole role,
        string prompt,
        string gbnfGrammar,
        CancellationToken ct = default) where T : class;
}
