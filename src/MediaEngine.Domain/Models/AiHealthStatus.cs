namespace MediaEngine.Domain.Models;

/// <summary>
/// Overall health status of the AI subsystem.
/// </summary>
public sealed class AiHealthStatus
{
    /// <summary>Per-role model statuses.</summary>
    public required IReadOnlyList<AiModelStatus> Models { get; init; }

    /// <summary>Estimated memory currently used by loaded models (MB).</summary>
    public int MemoryUsedMB { get; init; }

    /// <summary>Maximum memory budget from the selected memory profile (MB).</summary>
    public int MemoryLimitMB { get; init; }

    /// <summary>Whether a Vulkan-capable GPU was detected.</summary>
    public bool GpuAvailable { get; init; }

    /// <summary>Name of the active memory profile ("conservative", "balanced", "generous").</summary>
    public required string MemoryProfile { get; init; }

    /// <summary>True when all required models are downloaded and at least one is loadable.</summary>
    public bool IsReady { get; init; }
}
