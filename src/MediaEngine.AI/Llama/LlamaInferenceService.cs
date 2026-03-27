using System.Text;
using System.Text.Json;
using LLama;
using LLama.Common;
using LLama.Sampling;
using MediaEngine.AI.Configuration;
using MediaEngine.AI.Infrastructure;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Contracts;
using Microsoft.Extensions.Logging;

namespace MediaEngine.AI.Llama;

/// <summary>
/// Core LLM inference service wrapping LLamaSharp.
/// Handles model loading, GBNF grammar constraints, and structured JSON output.
/// Thread-safe: uses the ModelLifecycleManager's mutual exclusion.
/// </summary>
public sealed class LlamaInferenceService : ILlamaInferenceService
{
    private readonly AiSettings _settings;
    private readonly IModelLifecycleManager _lifecycle;
    private readonly ModelInventory _inventory;
    private readonly ILogger<LlamaInferenceService> _logger;

    // Cached model instances per role (populated on first use, cleared on unload).
    private readonly Dictionary<AiModelRole, (LLamaWeights Weights, ModelParams Params)> _loadedModels = new();
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public LlamaInferenceService(
        AiSettings settings,
        IModelLifecycleManager lifecycle,
        ModelInventory inventory,
        ILogger<LlamaInferenceService> logger)
    {
        _settings = settings;
        _lifecycle = lifecycle;
        _inventory = inventory;
        _logger = logger;
    }

    /// <summary>
    /// Run inference with a prompt and optional GBNF grammar constraint.
    /// Returns the raw text output from the LLM.
    /// </summary>
    /// <param name="role">Which model role to use (TextFast or TextQuality).</param>
    /// <param name="prompt">The full prompt text (system + user combined).</param>
    /// <param name="gbnfGrammar">Optional GBNF grammar string to constrain output.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Raw text output from the model.</returns>
    public async Task<string> InferAsync(
        AiModelRole role,
        string prompt,
        string? gbnfGrammar = null,
        CancellationToken ct = default)
    {
        if (role == AiModelRole.Audio)
            throw new ArgumentException("Use WhisperInferenceService for audio models", nameof(role));

        // Ensure the model is loaded.
        var loaded = await _lifecycle.EnsureLoadedAsync(role, ct);
        if (!loaded)
            throw new InvalidOperationException($"Failed to load model for role {role}");

        var (weights, modelParams) = await GetOrLoadWeightsAsync(role, ct);

        var definition = _inventory.GetDefinition(role);

        // Build inference parameters with optional GBNF grammar constraint.
        Grammar? grammar = !string.IsNullOrEmpty(gbnfGrammar)
            ? new Grammar(gbnfGrammar, "root")
            : null;

        var inferenceParams = new InferenceParams
        {
            MaxTokens = definition.MaxTokens > 0 ? definition.MaxTokens : 256,
            SamplingPipeline = grammar is not null
                ? new DefaultSamplingPipeline
                {
                    Temperature = (float)definition.Temperature,
                    Grammar = grammar,
                }
                : new DefaultSamplingPipeline
                {
                    Temperature = (float)definition.Temperature,
                },
        };

        // Create a stateless executor (no conversation history — one-shot).
        var executor = new StatelessExecutor(weights, modelParams)
        {
            ApplyTemplate = true,
        };

        // Run inference.
        var result = new StringBuilder();
        var timeout = TimeSpan.FromSeconds(_settings.InferenceTimeoutSeconds);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await foreach (var chunk in executor.InferAsync(prompt, inferenceParams, timeoutCts.Token))
            {
                result.Append(chunk);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Inference timed out after {Timeout}s for role {Role}",
                _settings.InferenceTimeoutSeconds, role);
        }

        var output = result.ToString().Trim();
        _logger.LogDebug("Inference complete for {Role}: {Length} chars", role, output.Length);
        return output;
    }

    /// <summary>
    /// Run inference and parse the result as JSON.
    /// Retries once with higher temperature if parsing fails.
    /// </summary>
    public async Task<T?> InferJsonAsync<T>(
        AiModelRole role,
        string prompt,
        string gbnfGrammar,
        CancellationToken ct = default) where T : class
    {
        // Attempt 1: standard temperature.
        var raw = await InferAsync(role, prompt, gbnfGrammar, ct);

        var parsed = TryParseJson<T>(raw);
        if (parsed is not null) return parsed;

        _logger.LogWarning("JSON parse failed for {Role}, retrying with higher temperature. Raw: {Raw}",
            role, raw.Length > 200 ? raw[..200] + "..." : raw);

        // Attempt 2: bump temperature slightly.
        var definition = _inventory.GetDefinition(role);
        var originalTemp = definition.Temperature;
        definition.Temperature = Math.Min(originalTemp + 0.2, 0.5);

        try
        {
            raw = await InferAsync(role, prompt, gbnfGrammar, ct);
            return TryParseJson<T>(raw);
        }
        finally
        {
            definition.Temperature = originalTemp;
        }
    }

    private static T? TryParseJson<T>(string raw) where T : class
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        try
        {
            // Try direct parse.
            return JsonSerializer.Deserialize<T>(raw, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
            });
        }
        catch
        {
            // Try extracting JSON from surrounding text.
            var start = raw.IndexOf('{');
            var end = raw.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                try
                {
                    return JsonSerializer.Deserialize<T>(raw[start..(end + 1)], new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        AllowTrailingCommas = true,
                    });
                }
                catch { /* fall through */ }
            }

            // Try array extraction.
            start = raw.IndexOf('[');
            end = raw.LastIndexOf(']');
            if (start >= 0 && end > start)
            {
                try
                {
                    return JsonSerializer.Deserialize<T>(raw[start..(end + 1)], new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        AllowTrailingCommas = true,
                    });
                }
                catch { /* fall through */ }
            }

            return null;
        }
    }

    private async Task<(LLamaWeights Weights, ModelParams Params)> GetOrLoadWeightsAsync(
        AiModelRole role,
        CancellationToken ct)
    {
        await _loadLock.WaitAsync(ct);
        try
        {
            if (_loadedModels.TryGetValue(role, out var cached))
                return cached;

            var modelPath = _inventory.GetModelPath(role);
            var definition = _inventory.GetDefinition(role);

            _logger.LogInformation("Loading LLamaSharp model for {Role}: {Path}", role, modelPath);

            // Determine GPU layer count: hardware tier policy takes priority over the
            // per-model config value, so the benchmark result automatically governs
            // how many layers are offloaded to the GPU.
            int gpuLayers = ResolveGpuLayerCount(definition.GpuLayers);

            var modelParams = new ModelParams(modelPath)
            {
                ContextSize   = (uint)definition.ContextLength,
                GpuLayerCount = gpuLayers,
            };

            var weights = LLamaWeights.LoadFromFile(modelParams);
            var entry = (weights, modelParams);
            _loadedModels[role] = entry;

            _logger.LogInformation("LLamaSharp model loaded for {Role}", role);
            return entry;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <summary>
    /// Resolve the number of GPU layers to offload, merging the hardware tier policy
    /// with the per-model config default.
    /// Priority: benchmarked hardware tier → per-model config value → 0 (CPU-only).
    /// -1 in the tier policy means "all layers" and is translated to 999 for LLamaSharp.
    /// </summary>
    private int ResolveGpuLayerCount(int configuredLayers)
    {
        var profile = _settings.HardwareProfile;

        // If the hardware benchmark has run and the backend is a GPU, use the tier policy.
        if (profile.BenchmarkedAt.HasValue
            && !string.Equals(profile.Backend, "cpu", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(profile.Tier))
        {
            var features = HardwareTierPolicy.GetFeatures(profile.Tier);
            if (features.MaxGpuLayers == -1)
                return 999; // All layers on GPU.
            if (features.MaxGpuLayers > 0)
                return features.MaxGpuLayers;

            // MaxGpuLayers == 0 means CPU-only for this tier even with a GPU backend.
            return 0;
        }

        // No benchmark yet — fall back to the per-model config value.
        return configuredLayers;
    }

    /// <summary>
    /// Dispose cached model weights when the lifecycle manager unloads.
    /// Called by the lifecycle manager.
    /// </summary>
    public void DisposeModel(AiModelRole role)
    {
        if (_loadedModels.Remove(role, out var entry))
        {
            entry.Weights.Dispose();
            _logger.LogInformation("Disposed LLamaSharp model for {Role}", role);
        }
    }
}
