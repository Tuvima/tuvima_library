using MediaEngine.Domain.Enums;

namespace MediaEngine.AI.Llama;

public interface ITextInferenceService
{
    Task<string> InferAsync(
        AiModelRole role,
        string prompt,
        string? gbnfGrammar = null,
        CancellationToken ct = default);

    Task<T?> InferJsonAsync<T>(
        AiModelRole role,
        string prompt,
        string gbnfGrammar,
        CancellationToken ct = default) where T : class;
}
