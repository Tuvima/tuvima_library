using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace MediaEngine.AI.Infrastructure;

/// <summary>
/// Monitors system resource usage to inform adaptive scheduling decisions.
/// Detects active transcoding, high CPU/RAM usage, and recommends whether
/// AI enrichment should proceed or yield.
/// </summary>
public sealed class ResourceMonitorService
{
    private readonly ILogger<ResourceMonitorService> _logger;
    private readonly Configuration.AiSettings _settings;

    public ResourceMonitorService(ILogger<ResourceMonitorService> logger, Configuration.AiSettings settings)
    {
        _logger = logger;
        _settings = settings;
    }

    /// <summary>
    /// Check current system load and return a resource snapshot.
    /// </summary>
    public ResourceSnapshot GetSnapshot()
    {
        var process = Process.GetCurrentProcess();
        var gcInfo  = GC.GetGCMemoryInfo();

        long totalRamMb = gcInfo.TotalAvailableMemoryBytes / (1024 * 1024);
        long usedRamMb  = process.WorkingSet64 / (1024 * 1024);
        long freeRamMb  = totalRamMb - usedRamMb;

        bool   transcodingActive = IsTranscodingActive();
        double cpuPressure       = EstimateCpuPressure();

        return new ResourceSnapshot
        {
            TotalRamMb       = totalRamMb,
            FreeRamMb        = freeRamMb,
            EngineRamMb      = usedRamMb,
            CpuPressure      = cpuPressure,
            TranscodingActive = transcodingActive,
            Timestamp        = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Recommend whether a given model size can be loaded right now.
    /// </summary>
    public LoadRecommendation CanLoadModel(int modelSizeMb)
    {
        var snapshot = GetSnapshot();

        // Don't load if transcoding is active and model is large (>2GB).
        if (snapshot.TranscodingActive && modelSizeMb > 2000)
            return new LoadRecommendation(false, "Transcoding active — deferring large model load");

        // Don't load if free RAM is less than model size + 2GB headroom.
        long requiredMb = modelSizeMb + 2048;
        if (snapshot.FreeRamMb < requiredMb)
            return new LoadRecommendation(false,
                $"Insufficient free RAM: {snapshot.FreeRamMb}MB free, need {requiredMb}MB");

        // Don't load if CPU pressure is very high and model is large.
        // Skip this check on High tier — inference runs on GPU, CPU pressure is irrelevant.
        var tier = _settings.HardwareProfile?.Tier ?? "auto";
        bool gpuAvailable = string.Equals(tier, HardwareTierPolicy.TierHigh, StringComparison.OrdinalIgnoreCase);
        if (!gpuAvailable && snapshot.CpuPressure > 0.85 && modelSizeMb > 2000)
            return new LoadRecommendation(false, "CPU pressure too high — deferring large model load");

        return new LoadRecommendation(true, "Resources available");
    }

    /// <summary>
    /// Check if FFmpeg, HandBrake, or other transcoding processes are running.
    /// </summary>
    private static bool IsTranscodingActive()
    {
        try
        {
            var transcoderNames = new[] { "ffmpeg", "HandBrakeCLI", "handbrake", "ffprobe" };
            var processes       = Process.GetProcesses();

            foreach (var proc in processes)
            {
                try
                {
                    var name = proc.ProcessName;
                    foreach (var transcoder in transcoderNames)
                    {
                        if (name.Contains(transcoder, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
                catch { /* Access denied for some system processes — ignore */ }
            }
        }
        catch { /* Process enumeration failed — assume not transcoding */ }

        return false;
    }

    /// <summary>
    /// Rough CPU pressure estimate (0.0 = idle, 1.0 = fully saturated).
    /// Uses thread count relative to logical processor count as a proxy.
    /// </summary>
    private static TimeSpan _lastCpuTime = TimeSpan.Zero;
    private static DateTime _lastCheckTime = DateTime.MinValue;

    /// <summary>
    /// Estimate CPU pressure using actual CPU time delta over the last sample interval.
    /// Returns 0.0 (idle) to 1.0 (all cores saturated).
    /// Thread count is NOT a valid proxy — a .NET process has 50+ threads at idle.
    /// </summary>
    private static double EstimateCpuPressure()
    {
        try
        {
            var process = Process.GetCurrentProcess();
            var currentCpuTime = process.TotalProcessorTime;
            var currentTime = DateTime.UtcNow;

            if (_lastCheckTime == DateTime.MinValue)
            {
                // First call — no delta yet, assume idle.
                _lastCpuTime = currentCpuTime;
                _lastCheckTime = currentTime;
                return 0.0;
            }

            var elapsed = (currentTime - _lastCheckTime).TotalSeconds;
            if (elapsed < 0.5) return 0.0; // Too short an interval

            var cpuDelta = (currentCpuTime - _lastCpuTime).TotalSeconds;
            int processorCount = Environment.ProcessorCount;

            _lastCpuTime = currentCpuTime;
            _lastCheckTime = currentTime;

            // CPU usage = cpu time consumed / (wall time × core count)
            return Math.Clamp(cpuDelta / (elapsed * processorCount), 0.0, 1.0);
        }
        catch { return 0.0; } // Unknown — assume idle (safe default for High tier)
    }
}

/// <summary>Snapshot of current system resource state.</summary>
public sealed class ResourceSnapshot
{
    public long          TotalRamMb        { get; set; }
    public long          FreeRamMb         { get; set; }
    public long          EngineRamMb       { get; set; }
    public double        CpuPressure       { get; set; }
    public bool          TranscodingActive  { get; set; }
    public DateTimeOffset Timestamp        { get; set; }
}

/// <summary>Recommendation on whether a model can be loaded.</summary>
public sealed record LoadRecommendation(bool CanLoad, string Reason);
