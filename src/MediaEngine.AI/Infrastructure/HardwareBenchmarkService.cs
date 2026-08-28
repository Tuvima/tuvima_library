using System.Diagnostics;
using MediaEngine.AI.Configuration;
using MediaEngine.AI.Llama;
using MediaEngine.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MediaEngine.AI.Infrastructure;

/// <summary>Runs and persists a versioned benchmark for the current physical machine.</summary>
public sealed class HardwareBenchmarkService
{
    private readonly LlamaInferenceService _llama;
    private readonly AiSettings _settings;
    private readonly GpuBackendDetector _gpuDetector;
    private readonly AiBenchmarkStateStore _stateStore;
    private readonly ILogger<HardwareBenchmarkService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public HardwareBenchmarkService(
        LlamaInferenceService llama,
        AiSettings settings,
        GpuBackendDetector gpuDetector,
        AiBenchmarkStateStore stateStore,
        ILogger<HardwareBenchmarkService> logger)
    {
        _llama = llama;
        _settings = settings;
        _gpuDetector = gpuDetector;
        _stateStore = stateStore;
        _logger = logger;
        var detected = _gpuDetector.Detect();
        Copy(_stateStore.LoadCurrent(detected.Backend, detected.GpuName), _settings.HardwareProfile);
    }

    public HardwareProfile Current => _settings.HardwareProfile;

    public async Task<HardwareProfile> BenchmarkAsync(bool force = false, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var detected = _gpuDetector.Detect();
            var fingerprint = AiBenchmarkStateStore.CreateMachineFingerprint(detected.Backend, detected.GpuName);
            var profile = _settings.HardwareProfile;
            if (!force
                && profile.Outcome == AiBenchmarkOutcomes.Succeeded
                && string.Equals(profile.MachineFingerprint, fingerprint, StringComparison.Ordinal))
            {
                return profile;
            }

            profile.MachineFingerprint = fingerprint;
            profile.Backend = detected.Backend;
            profile.GpuName = detected.GpuName;
            profile.AvailableRamMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);
            profile.BenchmarkModel = _settings.Models.TextFast.CatalogKey;
            profile.BenchmarkedAt = DateTime.UtcNow;
            profile.FailureCode = null;
            profile.FailureMessage = null;

            try
            {
                const string prompt = "Write a detailed paragraph about the history of libraries, including at least five key facts:";
                var sw = Stopwatch.StartNew();
                var result = await _llama.InferAsync(AiModelRole.TextFast, prompt, ct: ct).ConfigureAwait(false);
                sw.Stop();

                // Empty output is a completed zero-throughput measurement, not an execution failure.
                var estimatedTokens = string.IsNullOrEmpty(result) ? 0 : Math.Max(1, (int)(result.Length / 3.5));
                var throughput = estimatedTokens == 0
                    ? 0d
                    : estimatedTokens / Math.Max(sw.Elapsed.TotalSeconds, 0.1);
                profile.Outcome = AiBenchmarkOutcomes.Succeeded;
                profile.TokensPerSecond = throughput;
                profile.Tier = HardwareTierPolicy.ClassifyTier(
                    throughput,
                    profile.AvailableRamMb,
                    _gpuDetector.HasDedicatedGpu);
                _logger.LogInformation(
                    "AI benchmark succeeded on {Model}: {Throughput:F1} tok/s; tier={Tier}",
                    profile.BenchmarkModel,
                    throughput,
                    profile.Tier);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                profile.Outcome = AiBenchmarkOutcomes.Failed;
                profile.TokensPerSecond = null;
                profile.Tier = "auto";
                profile.FailureCode = "benchmark_execution_failed";
                profile.FailureMessage = ex.Message;
                _logger.LogWarning(ex, "AI benchmark execution failed");
            }

            _stateStore.Save(profile);
            _settings.ApplyEffectiveResourceProfile();
            return profile;
        }
        finally
        {
            _gate.Release();
        }
    }

    public HardwareProfile Invalidate()
    {
        var detected = _gpuDetector.Detect();
        var invalidated = _stateStore.Invalidate(detected.Backend, detected.GpuName);
        Copy(invalidated, _settings.HardwareProfile);
        _settings.ApplyEffectiveResourceProfile();
        return _settings.HardwareProfile;
    }

    private static void Copy(HardwareProfile source, HardwareProfile destination)
    {
        destination.Outcome = source.Outcome;
        destination.Tier = source.Tier;
        destination.Backend = source.Backend;
        destination.GpuName = source.GpuName;
        destination.TokensPerSecond = source.TokensPerSecond;
        destination.AvailableRamMb = source.AvailableRamMb;
        destination.BenchmarkedAt = source.BenchmarkedAt;
        destination.MachineFingerprint = source.MachineFingerprint;
        destination.BenchmarkModel = source.BenchmarkModel;
        destination.BenchmarkVersion = source.BenchmarkVersion;
        destination.FailureCode = source.FailureCode;
        destination.FailureMessage = source.FailureMessage;
    }
}
