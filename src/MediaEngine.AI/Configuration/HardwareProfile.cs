using System.Text.Json.Serialization;

namespace MediaEngine.AI.Configuration;

/// <summary>
/// Hardware profiling result — persisted to config/ai.json after benchmark.
/// Drives feature tier decisions across the entire AI subsystem.
/// </summary>
public sealed class HardwareProfile
{
    [JsonPropertyName("outcome")]
    public string Outcome { get; set; } = AiBenchmarkOutcomes.NotRun;

    [JsonPropertyName("tier")]
    public string Tier { get; set; } = "auto";

    [JsonPropertyName("backend")]
    public string Backend { get; set; } = "cpu";

    [JsonPropertyName("gpu_name")]
    public string? GpuName { get; set; }

    [JsonPropertyName("tokens_per_second")]
    public double? TokensPerSecond { get; set; }

    [JsonPropertyName("available_ram_mb")]
    public long AvailableRamMb { get; set; }

    [JsonPropertyName("benchmarked_at")]
    public DateTime? BenchmarkedAt { get; set; }

    [JsonPropertyName("machine_fingerprint")]
    public string? MachineFingerprint { get; set; }

    [JsonPropertyName("benchmark_model")]
    public string? BenchmarkModel { get; set; }

    [JsonPropertyName("benchmark_version")]
    public string BenchmarkVersion { get; set; } = "v2";

    [JsonPropertyName("failure_code")]
    public string? FailureCode { get; set; }

    [JsonPropertyName("failure_message")]
    public string? FailureMessage { get; set; }

    [JsonIgnore]
    public bool AdvancedEligible => Outcome == AiBenchmarkOutcomes.Succeeded
        && TokensPerSecond >= 15
        && AvailableRamMb >= 12_288;
}

public static class AiBenchmarkOutcomes
{
    public const string NotRun = "not_run";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Invalidated = "invalidated";
}
