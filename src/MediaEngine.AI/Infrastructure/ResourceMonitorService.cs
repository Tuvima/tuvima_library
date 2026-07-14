using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace MediaEngine.AI.Infrastructure;

/// <summary>Samples local resource pressure for adaptive AI scheduling.</summary>
public sealed class ResourceMonitorService
{
    private static readonly string[] TranscoderNames = ["ffmpeg", "HandBrakeCLI", "handbrake", "ffprobe"];

    private readonly ILogger<ResourceMonitorService> _logger;
    private readonly Configuration.AiSettings _settings;
    private readonly object _cpuSampleLock = new();
    private TimeSpan _lastCpuTime;
    private DateTime _lastCheckTime = DateTime.MinValue;

    public ResourceMonitorService(
        ILogger<ResourceMonitorService> logger,
        Configuration.AiSettings settings)
    {
        _logger = logger;
        _settings = settings;
    }

    public ResourceSnapshot GetSnapshot()
    {
        using var process = Process.GetCurrentProcess();
        var gcInfo = GC.GetGCMemoryInfo();
        var totalRamMb = gcInfo.TotalAvailableMemoryBytes / (1024 * 1024);
        var usedRamMb = process.WorkingSet64 / (1024 * 1024);

        return new ResourceSnapshot
        {
            TotalRamMb = totalRamMb,
            FreeRamMb = Math.Max(0, totalRamMb - usedRamMb),
            EngineRamMb = usedRamMb,
            CpuPressure = EstimateCpuPressure(),
            TranscodingActive = IsTranscodingActive(),
            Timestamp = DateTimeOffset.UtcNow,
        };
    }

    public LoadRecommendation CanLoadModel(int modelSizeMb)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(modelSizeMb);
        var snapshot = GetSnapshot();

        if (snapshot.TranscodingActive && modelSizeMb > 2000)
        {
            return new(false, "Transcoding active - deferring large model load");
        }

        var requiredMb = modelSizeMb + 2048L;
        if (snapshot.FreeRamMb < requiredMb)
        {
            return new(
                false,
                $"Insufficient free RAM: {snapshot.FreeRamMb}MB free, need {requiredMb}MB");
        }

        var tier = _settings.HardwareProfile?.Tier ?? "auto";
        var gpuAvailable = string.Equals(
            tier,
            HardwareTierPolicy.TierHigh,
            StringComparison.OrdinalIgnoreCase);
        if (!gpuAvailable && snapshot.CpuPressure > 0.85 && modelSizeMb > 2000)
        {
            return new(false, "CPU pressure too high - deferring large model load");
        }

        return new(true, "Resources available");
    }

    private bool IsTranscodingActive()
    {
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        var name = process.ProcessName;
                        if (TranscoderNames.Any(
                            transcoder => name.Contains(transcoder, StringComparison.OrdinalIgnoreCase)))
                        {
                            return true;
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // A process may exit between enumeration and inspection.
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        // Some system processes deny metadata access.
                    }
                }
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug(ex, "Could not enumerate processes while checking transcoding load");
        }

        return false;
    }

    private double EstimateCpuPressure()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            lock (_cpuSampleLock)
            {
                var currentCpuTime = process.TotalProcessorTime;
                var currentTime = DateTime.UtcNow;
                if (_lastCheckTime == DateTime.MinValue)
                {
                    _lastCpuTime = currentCpuTime;
                    _lastCheckTime = currentTime;
                    return 0.0;
                }

                var elapsed = (currentTime - _lastCheckTime).TotalSeconds;
                if (elapsed < 0.5)
                {
                    return 0.0;
                }

                var cpuDelta = (currentCpuTime - _lastCpuTime).TotalSeconds;
                _lastCpuTime = currentCpuTime;
                _lastCheckTime = currentTime;
                return Math.Clamp(
                    cpuDelta / (elapsed * Environment.ProcessorCount),
                    0.0,
                    1.0);
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug(ex, "Could not sample process CPU time");
            return 0.0;
        }
    }
}

public sealed class ResourceSnapshot
{
    public long TotalRamMb { get; set; }
    public long FreeRamMb { get; set; }
    public long EngineRamMb { get; set; }
    public double CpuPressure { get; set; }
    public bool TranscodingActive { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}

public sealed record LoadRecommendation(bool CanLoad, string Reason);
