using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Hardware profile returned by GET /ai/profile and POST /ai/benchmark.
/// </summary>
public sealed class HardwareProfileDto
{
    [JsonPropertyName("tier")]
    public string Tier { get; set; } = "unknown";

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

/// <summary>
/// Live resource snapshot returned by GET /ai/resources.
/// </summary>
public sealed class ResourceSnapshotDto
{
    [JsonPropertyName("total_ram_mb")]
    public long TotalRamMb { get; set; }

    [JsonPropertyName("free_ram_mb")]
    public long FreeRamMb { get; set; }

    [JsonPropertyName("engine_ram_mb")]
    public long EngineRamMb { get; set; }

    [JsonPropertyName("cpu_pressure")]
    public int CpuPressure { get; set; }

    [JsonPropertyName("transcoding_active")]
    public bool TranscodingActive { get; set; }
}

/// <summary>
/// AI enrichment queue progress returned by GET /ai/enrichment/progress.
/// </summary>
public sealed class EnrichmentProgressDto
{
    [JsonPropertyName("pending_count")]
    public int PendingCount { get; set; }

    [JsonPropertyName("completed_count")]
    public int CompletedCount { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }
}
