using System.Text.Json.Serialization;

namespace MediaEngine.AI.Configuration;

/// <summary>
/// Hardware profiling result — persisted to config/ai.json after benchmark.
/// Drives feature tier decisions across the entire AI subsystem.
/// </summary>
public sealed class HardwareProfile
{
    [JsonPropertyName("tier")]
    public string Tier { get; set; } = "auto";

    [JsonPropertyName("backend")]
    public string Backend { get; set; } = "cpu";

    [JsonPropertyName("gpu_name")]
    public string? GpuName { get; set; }

    [JsonPropertyName("tokens_per_second")]
    public double TokensPerSecond { get; set; }

    [JsonPropertyName("available_ram_mb")]
    public long AvailableRamMb { get; set; }

    [JsonPropertyName("benchmarked_at")]
    public DateTime? BenchmarkedAt { get; set; }
}
