using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.Ai;

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
    public string ModelsDirectory { get; set; } = "/models";

    [JsonPropertyName("idle_unload_seconds")]
    public int IdleUnloadSeconds { get; set; } = 300;

    [JsonPropertyName("inference_timeout_seconds")]
    public int InferenceTimeoutSeconds { get; set; } = 60;

    [JsonPropertyName("max_concurrent_inferences")]
    public int MaxConcurrentInferences { get; set; } = 1;

    [JsonPropertyName("minimum_free_disk_mb")]
    public int MinimumFreeDiskMB { get; set; } = 1024;

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
    public int EnrichmentBatchSize { get; set; } = 10;

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
    public string Status { get; set; } = "candidate";

    [JsonPropertyName("selection_tier")]
    public string SelectionTier { get; set; } = "candidate";

    [JsonPropertyName("intended_roles")]
    public List<string> IntendedRoles { get; set; } = [];

    [JsonPropertyName("file")]
    public string File { get; set; } = "";

    [JsonPropertyName("download_url")]
    public string DownloadUrl { get; set; } = "";

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    [JsonPropertyName("source_url")]
    public string SourceUrl { get; set; } = "";

    [JsonPropertyName("size_mb")]
    public int SizeMB { get; set; }

    [JsonPropertyName("parameters_b")]
    public double ParametersB { get; set; }

    [JsonPropertyName("effective_parameters_b")]
    public double? EffectiveParametersB { get; set; }

    [JsonPropertyName("quantization")]
    public string Quantization { get; set; } = "";

    [JsonPropertyName("context_length")]
    public int ContextLength { get; set; }

    [JsonPropertyName("memory_envelope_mb")]
    public int MemoryEnvelopeMB { get; set; }

    [JsonPropertyName("max_context_length")]
    public int MaxContextLength { get; set; }

    [JsonPropertyName("experimental")]
    public bool Experimental { get; set; }

    [JsonPropertyName("compatibility")]
    public AiModelCompatibilityDto Compatibility { get; set; } = new();

    [JsonPropertyName("readiness")]
    public AiModelReadinessDto Readiness { get; set; } = new();

    [JsonPropertyName("capabilities")]
    public AiModelCapabilitiesDto Capabilities { get; set; } = new();

    [JsonPropertyName("validation")]
    public AiModelValidationProfileDto Validation { get; set; } = new();

    [JsonPropertyName("selection_rationale")]
    public string SelectionRationale { get; set; } = "";

    [JsonPropertyName("integration_notes")]
    public string IntegrationNotes { get; set; } = "";
}

public sealed class AiModelCapabilitiesDto
{
    [JsonPropertyName("text_input")]
    public bool TextInput { get; set; }

    [JsonPropertyName("audio_input")]
    public bool AudioInput { get; set; }

    [JsonPropertyName("image_input")]
    public bool ImageInput { get; set; }

    [JsonPropertyName("text_output")]
    public bool TextOutput { get; set; } = true;

    [JsonPropertyName("structured_json")]
    public bool StructuredJson { get; set; }

    [JsonPropertyName("gbnf")]
    public bool Gbnf { get; set; }

    [JsonPropertyName("timestamp_segments")]
    public bool TimestampSegments { get; set; }

    [JsonPropertyName("word_timestamps")]
    public bool WordTimestamps { get; set; }

    [JsonPropertyName("sync_grade")]
    public bool SyncGrade { get; set; }

    [JsonPropertyName("multilingual")]
    public bool Multilingual { get; set; }

    [JsonPropertyName("cjk")]
    public bool Cjk { get; set; }

    [JsonPropertyName("experimental_multimodal")]
    public bool ExperimentalMultimodal { get; set; }

    [JsonPropertyName("embedding_output")]
    public bool EmbeddingOutput { get; set; }

    [JsonPropertyName("function_calling")]
    public bool FunctionCalling { get; set; }

    [JsonPropertyName("tool_calling")]
    public bool ToolCalling { get; set; }
}

public sealed class AiModelCompatibilityDto
{
    [JsonPropertyName("supported_backends")]
    public List<string> SupportedBackends { get; set; } = [];

    [JsonPropertyName("minimum_runtime_version")]
    public string? MinimumRuntimeVersion { get; set; }

    [JsonPropertyName("requires_mmproj")]
    public bool RequiresMmproj { get; set; }

    [JsonPropertyName("requires_audio_encoder")]
    public bool RequiresAudioEncoder { get; set; }
}

public sealed class AiModelReadinessDto
{
    [JsonPropertyName("configuration_ready")]
    public bool ConfigurationReady { get; set; }

    [JsonPropertyName("runtime_ready")]
    public bool RuntimeReady { get; set; }

    [JsonPropertyName("validated")]
    public bool Validated { get; set; }

    [JsonPropertyName("blocking_reasons")]
    public List<string> BlockingReasons { get; set; } = [];
}

public sealed class AiModelValidationProfileDto
{
    [JsonPropertyName("target_warm_latency_ms")]
    public int TargetWarmLatencyMs { get; set; }

    [JsonPropertyName("max_warm_latency_ms")]
    public int MaxWarmLatencyMs { get; set; }

    [JsonPropertyName("min_json_validity_rate")]
    public double MinJsonValidityRate { get; set; } = 0.99;

    [JsonPropertyName("min_task_pass_rate")]
    public double MinTaskPassRate { get; set; } = 0.9;

    [JsonPropertyName("max_hallucination_rate")]
    public double MaxHallucinationRate { get; set; }

    [JsonPropertyName("max_wer")]
    public double? MaxWer { get; set; }

    [JsonPropertyName("max_timestamp_drift_ms")]
    public int? MaxTimestampDriftMs { get; set; }

    [JsonPropertyName("benchmark_suite")]
    public string BenchmarkSuite { get; set; } = "";
}

public sealed class AiBenchmarkReportDto
{
    [JsonPropertyName("suiteKey")]
    public string SuiteKey { get; set; } = "";

    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("catalogKey")]
    public string CatalogKey { get; set; } = "";

    [JsonPropertyName("evaluatedAt")]
    public DateTimeOffset EvaluatedAt { get; set; }

    [JsonPropertyName("passed")]
    public bool Passed { get; set; }

    [JsonPropertyName("jsonValidityRate")]
    public double JsonValidityRate { get; set; }

    [JsonPropertyName("taskPassRate")]
    public double TaskPassRate { get; set; }

    [JsonPropertyName("hallucinationRate")]
    public double HallucinationRate { get; set; }

    [JsonPropertyName("worstWordErrorRate")]
    public double WorstWordErrorRate { get; set; }

    [JsonPropertyName("worstTimestampDriftMs")]
    public int WorstTimestampDriftMs { get; set; }

    [JsonPropertyName("worstLatencyMs")]
    public int WorstLatencyMs { get; set; }

    [JsonPropertyName("missingCases")]
    public List<string> MissingCases { get; set; } = [];

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
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("experimental")]
    public bool Experimental { get; set; }

    [JsonPropertyName("memory_envelope_mb")]
    public int MemoryEnvelopeMB { get; set; }

    [JsonPropertyName("max_context_length")]
    public int MaxContextLength { get; set; }

    [JsonPropertyName("max_output_tokens")]
    public int MaxOutputTokens { get; set; }

    [JsonPropertyName("temperature")]
    public double Temperature { get; set; }

    [JsonPropertyName("max_concurrency")]
    public int MaxConcurrency { get; set; }
}

public sealed class AiRoleRequirementDto
{
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("selection_policy")]
    public string SelectionPolicy { get; set; } = "Use the smallest model that passes all gates.";

    [JsonPropertyName("required_capabilities")]
    public List<string> RequiredCapabilities { get; set; } = [];

    [JsonPropertyName("preferred_catalog_keys")]
    public List<string> PreferredCatalogKeys { get; set; } = [];

    [JsonPropertyName("fallback_catalog_keys")]
    public List<string> FallbackCatalogKeys { get; set; } = [];

    [JsonPropertyName("max_default_size_mb")]
    public int MaxDefaultSizeMB { get; set; }

    [JsonPropertyName("target_warm_latency_ms")]
    public int TargetWarmLatencyMs { get; set; }

    [JsonPropertyName("max_background_latency_ms")]
    public int MaxBackgroundLatencyMs { get; set; }

    [JsonPropertyName("min_json_validity_rate")]
    public double MinJsonValidityRate { get; set; } = 0.99;

    [JsonPropertyName("min_task_pass_rate")]
    public double MinTaskPassRate { get; set; } = 0.9;

    [JsonPropertyName("benchmark_suite")]
    public string BenchmarkSuite { get; set; } = "";

    [JsonPropertyName("memory_envelope_mb")]
    public int MemoryEnvelopeMB { get; set; }

    [JsonPropertyName("max_context_length")]
    public int MaxContextLength { get; set; }

    [JsonPropertyName("experimental_allowed")]
    public bool ExperimentalAllowed { get; set; }
}

/// <summary>
/// Hardware profile returned by GET /ai/profile and POST /ai/benchmark.
/// </summary>
public sealed class HardwareProfileDto
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
    public double CpuPressure { get; set; }

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

public sealed class AiBenchmarkRunRequest
{
    [JsonPropertyName("catalogKey")]
    public string CatalogKey { get; set; } = "";

    [JsonPropertyName("allowHardwareBenchmark")]
    public bool AllowHardwareBenchmark { get; set; }

    [JsonPropertyName("allowModelExecution")]
    public bool AllowModelExecution { get; set; }
}

public sealed class AiBenchmarkSuiteDto
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("gates")]
    public AiBenchmarkGatesDto Gates { get; set; } = new();

    [JsonPropertyName("cases")]
    public List<AiBenchmarkCaseDto> Cases { get; set; } = [];
}

public sealed class AiBenchmarkGatesDto
{
    [JsonPropertyName("targetWarmLatencyMs")]
    public int TargetWarmLatencyMs { get; set; }

    [JsonPropertyName("maxWarmLatencyMs")]
    public int MaxWarmLatencyMs { get; set; }

    [JsonPropertyName("minJsonValidityRate")]
    public double MinJsonValidityRate { get; set; }

    [JsonPropertyName("minTaskPassRate")]
    public double MinTaskPassRate { get; set; }

    [JsonPropertyName("maxHallucinationRate")]
    public double MaxHallucinationRate { get; set; }

    [JsonPropertyName("maxWer")]
    public double? MaxWer { get; set; }

    [JsonPropertyName("maxTimestampDriftMs")]
    public int? MaxTimestampDriftMs { get; set; }
}

public sealed class AiBenchmarkCaseDto
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("feature")]
    public string Feature { get; set; } = "";

    [JsonPropertyName("fixtureDescription")]
    public string FixtureDescription { get; set; } = "";

    [JsonPropertyName("requiresJson")]
    public bool RequiresJson { get; set; }

    [JsonPropertyName("fixtureInputJson")]
    public string FixtureInputJson { get; set; } = "{}";

    [JsonPropertyName("expectedAssertions")]
    public List<AiBenchmarkAssertionDto>? ExpectedAssertions { get; set; }

    [JsonPropertyName("expectedRootProperties")]
    public List<string>? ExpectedRootProperties { get; set; }

    [JsonPropertyName("assertions")]
    public List<AiBenchmarkAssertionDto> Assertions { get; set; } = [];

    [JsonPropertyName("allowedRootProperties")]
    public List<string> AllowedRootProperties { get; set; } = [];
}

public sealed class AiBenchmarkAssertionDto
{
    [JsonPropertyName("property")]
    public string Property { get; set; } = "";

    [JsonPropertyName("expectedValue")]
    public string? ExpectedValue { get; set; }
}

public sealed class IntentSearchRequest
{
    [JsonPropertyName("query")]
    public string Query { get; set; } = "";
}

public sealed class IntentSearchResponse
{
    [JsonPropertyName("genres")]
    public IReadOnlyList<string> Genres { get; set; } = [];

    [JsonPropertyName("moods")]
    public IReadOnlyList<string> Moods { get; set; } = [];

    [JsonPropertyName("yearFrom")]
    public int? YearFrom { get; set; }

    [JsonPropertyName("yearTo")]
    public int? YearTo { get; set; }

    [JsonPropertyName("mediaTypes")]
    public IReadOnlyList<MediaEngine.Domain.Enums.MediaType> MediaTypes { get; set; } = [];

    [JsonPropertyName("keywords")]
    public IReadOnlyList<string> Keywords { get; set; } = [];

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("originalQuery")]
    public string OriginalQuery { get; set; } = "";
}

public sealed class UrlExtractRequest
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";
}

public sealed class UrlExtractionResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("fields")]
    public IReadOnlyDictionary<string, string> Fields { get; set; } =
        new Dictionary<string, string>();

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}
