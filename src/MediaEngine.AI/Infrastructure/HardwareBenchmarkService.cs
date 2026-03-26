using System.Diagnostics;
using MediaEngine.AI.Configuration;
using MediaEngine.AI.Llama;
using MediaEngine.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MediaEngine.AI.Infrastructure;

/// <summary>
/// Runs a hardware benchmark at startup to classify the system into a performance tier.
/// Results are cached in AiSettings.HardwareProfile so the benchmark only runs once
/// unless the user explicitly re-runs it.
/// </summary>
public sealed class HardwareBenchmarkService
{
    private readonly LlamaInferenceService              _llama;
    private readonly AiSettings                         _settings;
    private readonly GpuBackendDetector                 _gpuDetector;
    private readonly ILogger<HardwareBenchmarkService>  _logger;

    public HardwareBenchmarkService(
        LlamaInferenceService              llama,
        AiSettings                         settings,
        GpuBackendDetector                 gpuDetector,
        ILogger<HardwareBenchmarkService>  logger)
    {
        _llama       = llama;
        _settings    = settings;
        _gpuDetector = gpuDetector;
        _logger      = logger;
    }

    /// <summary>
    /// Run the benchmark if not already cached (or if tier is "auto").
    /// Returns the resolved hardware profile.
    /// </summary>
    public async Task<HardwareProfile> BenchmarkAsync(CancellationToken ct = default)
    {
        var profile = _settings.HardwareProfile;

        // Skip if already benchmarked and not set to "auto".
        if (profile.BenchmarkedAt.HasValue
            && !string.Equals(profile.Tier, "auto", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Hardware profile cached: tier={Tier}, backend={Backend}, {TokPerSec:F1} tok/s (benchmarked {When})",
                profile.Tier, profile.Backend, profile.TokensPerSecond, profile.BenchmarkedAt);
            return profile;
        }

        _logger.LogInformation("Running hardware benchmark...");

        // Measure available RAM.
        var availableRam = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);
        profile.AvailableRamMb = availableRam;

        // Detect GPU backend via Vulkan → CUDA → CPU probe chain.
        var (backend, gpuName) = _gpuDetector.Detect();
        profile.Backend = backend;
        profile.GpuName = gpuName;

        bool hasDedicatedGpu = _gpuDetector.HasDedicatedGpu;
        _logger.LogInformation(
            "GPU detection result: backend={Backend}, name={GpuName}, dedicated={Dedicated}",
            backend, gpuName ?? "none", hasDedicatedGpu);

        // Run token generation benchmark.
        // Generate enough text to get a stable measurement (~50 tokens).
        double tokPerSec = 0;
        try
        {
            const string testPrompt = "Write a detailed paragraph about the history of libraries, including at least five key facts:";
            var sw     = Stopwatch.StartNew();
            var result = await _llama.InferAsync(AiModelRole.TextFast, testPrompt, ct: ct);
            sw.Stop();

            if (!string.IsNullOrEmpty(result))
            {
                // LLaMA tokenizer averages ~3.5 chars per token for English prose.
                // Use 3.5 for a more accurate estimate than 4.
                int estimatedTokens = Math.Max(1, (int)(result.Length / 3.5));
                tokPerSec = estimatedTokens / Math.Max(sw.Elapsed.TotalSeconds, 0.1);

                _logger.LogInformation(
                    "Benchmark inference: {Chars} chars ≈ {Tokens} tokens in {Elapsed:F1}s → {TokPerSec:F1} tok/s",
                    result.Length, estimatedTokens, sw.Elapsed.TotalSeconds, tokPerSec);
            }
            else
            {
                _logger.LogWarning("Benchmark inference returned empty result");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Benchmark inference failed — assuming minimal tier");
            tokPerSec = 0;
        }

        profile.TokensPerSecond = tokPerSec;

        // Classify tier using dedicated GPU flag (integrated GPUs do not qualify for AI acceleration).
        profile.Tier          = HardwareTierPolicy.ClassifyTier(tokPerSec, availableRam, hasDedicatedGpu);
        profile.BenchmarkedAt = DateTime.UtcNow;

        var features = HardwareTierPolicy.GetFeatures(profile.Tier);

        _logger.LogInformation(
            "Hardware benchmark complete: tier={Tier} ({Backend}{Gpu}, {TokPerSec:F1} tok/s, {Ram}MB RAM) " +
            "→ Ingestion AI: {Ingestion}, Enrichment: {Enrichment}, Whisper: {Whisper}",
            profile.Tier,
            profile.Backend,
            gpuName is not null ? $" — {gpuName}" : "",
            profile.TokensPerSecond,
            profile.AvailableRamMb,
            features.SmartLabelerEnabled ? "enabled" : "disabled",
            features.EnrichmentMode,
            features.WhisperEnabled ? "enabled" : "disabled");

        return profile;
    }

}
