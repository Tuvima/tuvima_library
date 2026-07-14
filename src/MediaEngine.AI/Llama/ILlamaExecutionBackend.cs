using System.Runtime.CompilerServices;
using LLama;
using LLama.Common;
using LLama.Sampling;
using MediaEngine.AI.Configuration;
using MediaEngine.AI.Infrastructure;
using MediaEngine.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MediaEngine.AI.Llama;

internal interface ILlamaExecutionBackend : IAsyncDisposable
{
    IAsyncEnumerable<string> InferAsync(
        AiModelRole role,
        string prompt,
        string? gbnfGrammar,
        InferenceRequestOptions options,
        CancellationToken cancellationToken);

    ValueTask DisposeModelAsync(AiModelRole role, CancellationToken cancellationToken);
}

internal sealed class LlamaSharpExecutionBackend : ILlamaExecutionBackend
{
    private readonly AiSettings _settings;
    private readonly ModelInventory _inventory;
    private readonly ILogger<LlamaInferenceService> _logger;
    private readonly Dictionary<AiModelRole, (LLamaWeights Weights, ModelParams Params)> _loadedModels = [];
    private readonly SemaphoreSlim _modelGate = new(1, 1);
    private int _disposed;

    public LlamaSharpExecutionBackend(
        AiSettings settings,
        ModelInventory inventory,
        ILogger<LlamaInferenceService> logger)
    {
        _settings = settings;
        _inventory = inventory;
        _logger = logger;
    }

    public async IAsyncEnumerable<string> InferAsync(
        AiModelRole role,
        string prompt,
        string? gbnfGrammar,
        InferenceRequestOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var (weights, modelParams) = await GetOrLoadWeightsAsync(role, cancellationToken).ConfigureAwait(false);

        Grammar? grammar = !string.IsNullOrWhiteSpace(gbnfGrammar)
            ? new Grammar(gbnfGrammar, "root")
            : null;
        var pipeline = new DefaultSamplingPipeline
        {
            Temperature = (float)options.Temperature,
            Grammar = grammar,
        };
        var inferenceParams = new InferenceParams
        {
            MaxTokens = options.MaxTokens,
            SamplingPipeline = pipeline,
        };
        var executor = new StatelessExecutor(weights, modelParams)
        {
            ApplyTemplate = true,
        };

        await foreach (var chunk in executor
            .InferAsync(prompt, inferenceParams, cancellationToken)
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            yield return chunk;
        }
    }

    public async ValueTask DisposeModelAsync(AiModelRole role, CancellationToken cancellationToken)
    {
        await _modelGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_loadedModels.Remove(role, out var entry))
            {
                entry.Weights.Dispose();
                _logger.LogInformation("Disposed LLamaSharp model for {Role}", role);
            }
        }
        finally
        {
            _modelGate.Release();
        }
    }

    private async Task<(LLamaWeights Weights, ModelParams Params)> GetOrLoadWeightsAsync(
        AiModelRole role,
        CancellationToken cancellationToken)
    {
        await _modelGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_loadedModels.TryGetValue(role, out var cached))
            {
                return cached;
            }

            var modelPath = _inventory.GetModelPath(role);
            var definition = _inventory.GetDefinition(role);
            var forceCpu = role == AiModelRole.TextFast;
            var gpuLayers = forceCpu ? 0 : ResolveGpuLayerCount(definition.GpuLayers);
            var modelParams = new ModelParams(modelPath)
            {
                ContextSize = (uint)definition.ContextLength,
                GpuLayerCount = gpuLayers,
            };

            _logger.LogInformation(
                "Loading LLamaSharp model for {Role}: {Path} (gpu_layers={GpuLayers})",
                role,
                modelPath,
                gpuLayers);
            var weights = LLamaWeights.LoadFromFile(modelParams);
            var entry = (weights, modelParams);
            _loadedModels[role] = entry;
            return entry;
        }
        finally
        {
            _modelGate.Release();
        }
    }

    private int ResolveGpuLayerCount(int configuredLayers)
    {
        var profile = _settings.HardwareProfile;
        if (profile.BenchmarkedAt.HasValue
            && !string.Equals(profile.Backend, "cpu", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(profile.Tier))
        {
            var features = HardwareTierPolicy.GetFeatures(profile.Tier);
            return features.MaxGpuLayers switch
            {
                -1 => 999,
                > 0 => features.MaxGpuLayers,
                _ => 0,
            };
        }

        return configuredLayers;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _modelGate.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var entry in _loadedModels.Values)
            {
                entry.Weights.Dispose();
            }

            _loadedModels.Clear();
        }
        finally
        {
            _modelGate.Release();
            _modelGate.Dispose();
        }
    }
}
