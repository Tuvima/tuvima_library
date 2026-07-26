namespace MediaEngine.Contracts.Ai;

/// <summary>
/// Named wire responses for the AI administration endpoints. Member names deliberately
/// preserve the exact casing of the anonymous response objects they replace.
/// </summary>
public sealed record AiHealthStatusResponse(
    IReadOnlyList<AiModelStatusResponse> models,
    int memoryUsedMB,
    int memoryLimitMB,
    bool gpuAvailable,
    string memoryProfile,
    bool isReady);

public sealed record AiModelStatusResponse(
    string Role,
    string RoleName,
    bool Supported,
    string ModelType,
    string State,
    string Description,
    string ModelFile,
    int SizeMB,
    string? DownloadUrlHost,
    int DownloadProgressPercent,
    long BytesDownloaded,
    long TotalBytes,
    bool Loaded,
    bool Active,
    int MemoryFootprintMB,
    string RequiredHardwareTier,
    string? ErrorMessage,
    string? CatalogKey,
    string DisplayName,
    string Family,
    string Provider,
    string License,
    string Runtime,
    string SelectionTier,
    string SelectionStatus,
    string SelectionRationale,
    string RoleRequirement,
    string BenchmarkSuite,
    IReadOnlyList<string> ValidationWarnings,
    IReadOnlyList<string> Capabilities,
    string DiskStatus,
    long DiskSizeMB,
    int MemoryEnvelopeMB,
    string Quantization,
    string SourceUrl,
    string ChecksumStatus,
    bool ConfigurationReady,
    bool RuntimeReady,
    bool Validated,
    bool CanOperate,
    bool Experimental,
    IReadOnlyList<string> BlockingReasons);

public sealed record AiDownloadCancelledResponse(bool cancelled, string role);

public sealed record AiModelLoadedResponse(bool loaded, string role);

public sealed record AiModelUnloadedResponse(bool unloaded, string role);

public sealed record AiSettingsSavedResponse(bool saved);

public sealed record AiHardwareProfileResponse(
    string tier,
    string backend,
    string? gpu_name,
    double tokens_per_second,
    long available_ram_mb,
    DateTime? benchmarked_at);

public sealed record AiBenchmarkSuiteResponse<TGates, TCase>(
    string key,
    string role,
    TGates gates,
    IReadOnlyList<TCase> cases);

public sealed record AiResourceSnapshotResponse(
    long total_ram_mb,
    long free_ram_mb,
    long engine_ram_mb,
    double cpu_pressure,
    bool transcoding_active);

public sealed record AiEnrichmentProgressResponse(
    int pending_count,
    int completed_count,
    int total);
