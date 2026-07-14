using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

public sealed class AiHealthStatusDto
{
    [JsonPropertyName("models")]
    public List<AiModelStatusDto> Models { get; set; } = [];

    [JsonPropertyName("memoryUsedMB")]
    public int MemoryUsedMB { get; set; }

    [JsonPropertyName("memoryLimitMB")]
    public int MemoryLimitMB { get; set; }

    [JsonPropertyName("gpuAvailable")]
    public bool GpuAvailable { get; set; }

    [JsonPropertyName("memoryProfile")]
    public string MemoryProfile { get; set; } = "unknown";

    [JsonPropertyName("isReady")]
    public bool IsReady { get; set; }
}

public sealed class AiModelStatusDto
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("roleName")]
    public string RoleName { get; set; } = "";

    [JsonPropertyName("supported")]
    public bool Supported { get; set; } = true;

    [JsonPropertyName("modelType")]
    public string ModelType { get; set; } = "";

    [JsonPropertyName("state")]
    public string State { get; set; } = "NotDownloaded";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("modelFile")]
    public string ModelFile { get; set; } = "";

    [JsonPropertyName("sizeMB")]
    public int SizeMB { get; set; }

    [JsonPropertyName("downloadUrlHost")]
    public string? DownloadUrlHost { get; set; }

    [JsonPropertyName("downloadProgressPercent")]
    public int DownloadProgressPercent { get; set; }

    [JsonPropertyName("bytesDownloaded")]
    public long BytesDownloaded { get; set; }

    [JsonPropertyName("totalBytes")]
    public long TotalBytes { get; set; }

    [JsonPropertyName("loaded")]
    public bool Loaded { get; set; }

    [JsonPropertyName("active")]
    public bool Active { get; set; }

    [JsonPropertyName("memoryFootprintMB")]
    public int MemoryFootprintMB { get; set; }

    [JsonPropertyName("requiredHardwareTier")]
    public string RequiredHardwareTier { get; set; } = "low";

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("catalogKey")]
    public string? CatalogKey { get; set; }

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("family")]
    public string Family { get; set; } = "";

    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "";

    [JsonPropertyName("license")]
    public string License { get; set; } = "";

    [JsonPropertyName("runtime")]
    public string Runtime { get; set; } = "";

    [JsonPropertyName("selectionTier")]
    public string SelectionTier { get; set; } = "";

    [JsonPropertyName("selectionStatus")]
    public string SelectionStatus { get; set; } = "";

    [JsonPropertyName("selectionRationale")]
    public string SelectionRationale { get; set; } = "";

    [JsonPropertyName("roleRequirement")]
    public string RoleRequirement { get; set; } = "";

    [JsonPropertyName("benchmarkSuite")]
    public string BenchmarkSuite { get; set; } = "";

    [JsonPropertyName("validationWarnings")]
    public List<string> ValidationWarnings { get; set; } = [];

    [JsonPropertyName("capabilities")]
    public List<string> Capabilities { get; set; } = [];

    [JsonPropertyName("diskStatus")]
    public string DiskStatus { get; set; } = "unknown";

    [JsonPropertyName("diskSizeMB")]
    public long DiskSizeMB { get; set; }

    [JsonPropertyName("memoryEnvelopeMB")]
    public int MemoryEnvelopeMB { get; set; }

    [JsonPropertyName("quantization")]
    public string Quantization { get; set; } = "";

    [JsonPropertyName("sourceUrl")]
    public string SourceUrl { get; set; } = "";

    [JsonPropertyName("checksumStatus")]
    public string ChecksumStatus { get; set; } = "unknown";

    [JsonPropertyName("configurationReady")]
    public bool ConfigurationReady { get; set; }

    [JsonPropertyName("runtimeReady")]
    public bool RuntimeReady { get; set; }

    [JsonPropertyName("validated")]
    public bool Validated { get; set; }

    [JsonPropertyName("canOperate")]
    public bool CanOperate { get; set; } = true;

    [JsonPropertyName("experimental")]
    public bool Experimental { get; set; }

    [JsonPropertyName("blockingReasons")]
    public List<string> BlockingReasons { get; set; } = [];
}

public sealed class AiConfigDto
{
    [JsonPropertyName("dev_skip_download")]
    public bool DevSkipDownload { get; set; }

    [JsonPropertyName("models_directory")]
    public string ModelsDirectory { get; set; } = "";

    [JsonPropertyName("idle_unload_seconds")]
    public int IdleUnloadSeconds { get; set; }

    [JsonPropertyName("inference_timeout_seconds")]
    public int InferenceTimeoutSeconds { get; set; }

    [JsonPropertyName("models")]
    public Dictionary<string, AiModelDefinitionDto> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("model_catalog")]
    public Dictionary<string, AiModelCatalogEntryDto> ModelCatalog { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("operational_roles")]
    public Dictionary<string, AiOperationalRoleDto> OperationalRoles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("role_requirements")]
    public Dictionary<string, AiRoleRequirementDto> RoleRequirements { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("features")]
    public Dictionary<string, bool> Features { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("vibe_vocabulary")]
    public Dictionary<string, List<string>> VibeVocabulary { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("scheduling")]
    public Dictionary<string, object?> Scheduling { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("enrichment_batch_size")]
    public int EnrichmentBatchSize { get; set; }

    [JsonPropertyName("hardware_profile")]
    public HardwareProfileDto HardwareProfile { get; set; } = new();
}

public sealed class AiModelDefinitionDto
{
    [JsonPropertyName("catalog_key")]
    public string? CatalogKey { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("file")]
    public string File { get; set; } = "";

    [JsonPropertyName("download_url")]
    public string DownloadUrl { get; set; } = "";

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    [JsonPropertyName("size_mb")]
    public int SizeMB { get; set; }

    [JsonPropertyName("context_length")]
    public int ContextLength { get; set; }

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; }

    [JsonPropertyName("temperature")]
    public double Temperature { get; set; }

    [JsonPropertyName("gpu_layers")]
    public int GpuLayers { get; set; }

    [JsonPropertyName("threads")]
    public int Threads { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("translate")]
    public bool Translate { get; set; }
}

public sealed class AiModelCatalogEntryDto
{
    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("family")]
    public string Family { get; set; } = "";

    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "";

    [JsonPropertyName("license")]
    public string License { get; set; } = "";

    [JsonPropertyName("runtime")]
    public string Runtime { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("selection_tier")]
    public string SelectionTier { get; set; } = "";

    [JsonPropertyName("size_mb")]
    public int SizeMB { get; set; }

    [JsonPropertyName("selection_rationale")]
    public string SelectionRationale { get; set; } = "";
}

public sealed class AiProblemDetailsDto
{
    public string Type { get; init; } = "about:blank";
    public string Title { get; init; } = "AI operation failed";
    public int? Status { get; init; }
    public string Detail { get; init; } = "The Engine could not complete the operation.";
    public List<string> BlockingReasons { get; init; } = [];

    public string ToUserMessage()
    {
        var parts = new List<string> { Title };
        if (!string.IsNullOrWhiteSpace(Detail) && !string.Equals(Detail, Title, StringComparison.Ordinal))
            parts.Add(Detail);
        parts.AddRange(BlockingReasons.Where(reason => !string.IsNullOrWhiteSpace(reason)));
        return string.Join(" ", parts.Distinct(StringComparer.Ordinal));
    }
}

public sealed record AiOperationResultDto(bool Succeeded, AiProblemDetailsDto? Problem = null)
{
    public static AiOperationResultDto Success() => new(true);
    public static AiOperationResultDto Failure(AiProblemDetailsDto problem) => new(false, problem);
}

public sealed record AiOperationResultDto<T>(bool Succeeded, T? Value = default, AiProblemDetailsDto? Problem = null)
{
    public static AiOperationResultDto<T> Success(T value) => new(true, value);
    public static AiOperationResultDto<T> Failure(AiProblemDetailsDto problem) => new(false, default, problem);
}

public sealed class AiBenchmarkReportDto
{
    [JsonPropertyName("suiteKey")]
    public string SuiteKey { get; set; } = "";

    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("catalogKey")]
    public string CatalogKey { get; set; } = "";

    [JsonPropertyName("passed")]
    public bool Passed { get; set; }

    [JsonPropertyName("failures")]
    public List<string> Failures { get; set; } = [];
}

public sealed class AiOperationalRoleDto
{
    [JsonPropertyName("catalog_key")]
    public string CatalogKey { get; set; } = "";

    [JsonPropertyName("runtime_kind")]
    public string RuntimeKind { get; set; } = "text";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("experimental")]
    public bool Experimental { get; set; }

    [JsonPropertyName("memory_envelope_mb")]
    public int MemoryEnvelopeMB { get; set; }

    [JsonPropertyName("max_context_length")]
    public int MaxContextLength { get; set; }
}

public sealed class AiRoleRequirementDto
{
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("selection_policy")]
    public string SelectionPolicy { get; set; } = "";

    [JsonPropertyName("preferred_catalog_keys")]
    public List<string> PreferredCatalogKeys { get; set; } = [];

    [JsonPropertyName("fallback_catalog_keys")]
    public List<string> FallbackCatalogKeys { get; set; } = [];

    [JsonPropertyName("max_default_size_mb")]
    public int MaxDefaultSizeMB { get; set; }

    [JsonPropertyName("benchmark_suite")]
    public string BenchmarkSuite { get; set; } = "";
}

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
