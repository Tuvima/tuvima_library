using MediaEngine.AI.Llama;
using MediaEngine.Domain.Enums;
using MediaEngine.Plugins;

namespace MediaEngine.Api.Services.Plugins;

internal sealed class PluginAiClient
{
    private readonly ILlamaInferenceService _llama;

    public PluginAiClient(ILlamaInferenceService llama)
    {
        _llama = llama;
    }

    public async Task<string?> InferTextAsync(
        string role,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        var modelRole = ResolveRole(role);
        return await _llama.InferAsync(modelRole, prompt, ct: cancellationToken).ConfigureAwait(false);
    }

    public async Task<T?> InferJsonAsync<T>(
        string role,
        string prompt,
        string grammar,
        CancellationToken cancellationToken = default) where T : class
    {
        var modelRole = ResolveRole(role);
        return await _llama.InferJsonAsync<T>(modelRole, prompt, grammar, cancellationToken).ConfigureAwait(false);
    }

    private static AiModelRole ResolveRole(string role) =>
        role.Trim().ToLowerInvariant() switch
        {
            "text_fast" => AiModelRole.TextFast,
            "text_quality" => AiModelRole.TextQuality,
            "text_scholar" => AiModelRole.TextScholar,
            "text_cjk" => AiModelRole.TextCjk,
            "audio" => throw new NotSupportedException("Plugin audio AI access is reserved for a future Whisper bridge and is not exposed through the text LLM client."),
            "vision" => throw new NotSupportedException("Plugin vision access is reserved for a future multimodal runtime and is not exposed through the text LLM client."),
            _ => throw new ArgumentOutOfRangeException(nameof(role), $"Unknown AI role '{role}'."),
        };
}

