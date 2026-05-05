using MediaEngine.AI.Llama;
using MediaEngine.Domain.Enums;
using MediaEngine.Plugins;

namespace MediaEngine.Api.Services.Plugins;

public sealed class PluginAiClient : IPluginAiClient
{
    private readonly ILlamaInferenceService _llama;
    private readonly PluginCatalog _catalog;

    public PluginAiClient(ILlamaInferenceService llama, PluginCatalog catalog)
    {
        _llama = llama;
        _catalog = catalog;
    }

    public async Task<string?> InferTextAsync(
        string pluginId,
        string role,
        string prompt,
        PluginAiOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var modelRole = ValidateRole(pluginId, role, options);
        return await _llama.InferAsync(modelRole, prompt, ct: cancellationToken).ConfigureAwait(false);
    }

    public async Task<T?> InferJsonAsync<T>(
        string pluginId,
        string role,
        string prompt,
        string grammar,
        PluginAiOptions? options = null,
        CancellationToken cancellationToken = default) where T : class
    {
        var modelRole = ValidateRole(pluginId, role, options);
        return await _llama.InferJsonAsync<T>(modelRole, prompt, grammar, cancellationToken).ConfigureAwait(false);
    }

    private AiModelRole ValidateRole(string pluginId, string role, PluginAiOptions? options)
    {
        var plugin = _catalog.Get(pluginId)
            ?? throw new InvalidOperationException($"Plugin '{pluginId}' is not registered.");
        var permission = plugin.Manifest.AiPermissions.FirstOrDefault(p =>
            string.Equals(p.Role, role, StringComparison.OrdinalIgnoreCase));
        if (permission is null)
            throw new UnauthorizedAccessException($"Plugin '{pluginId}' is not allowed to use AI role '{role}'.");

        if (string.Equals(options?.Schedule, "on-demand", StringComparison.OrdinalIgnoreCase)
            && (string.Equals(role, "text_scholar", StringComparison.OrdinalIgnoreCase)
                || string.Equals(permission.ResourceClass, "ai-heavy", StringComparison.OrdinalIgnoreCase)))
        {
            throw new UnauthorizedAccessException("Heavy AI roles cannot be used by on-demand plugin calls.");
        }

        return role.Trim().ToLowerInvariant() switch
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
}

