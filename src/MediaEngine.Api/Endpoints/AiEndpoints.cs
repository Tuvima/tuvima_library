using MediaEngine.AI.Configuration;
using MediaEngine.AI.Infrastructure;
using MediaEngine.Api.Security;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Endpoints;

/// <summary>
/// AI subsystem API endpoints — model lifecycle, download management, and configuration.
///
/// All endpoints require Administrator role.
/// </summary>
internal static class AiEndpoints
{
    internal static RouteGroupBuilder MapAiEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/ai")
                          .WithTags("AI");

        // ── GET /ai/status ───────────────────────────────────────────────────
        group.MapGet("/status", (
            IModelLifecycleManager lifecycle) =>
        {
            var status = lifecycle.GetHealthStatus();
            return Results.Ok(new
            {
                models = status.Models.Select(ToModelStatusResponse),
                memoryUsedMB = status.MemoryUsedMB,
                memoryLimitMB = status.MemoryLimitMB,
                gpuAvailable = status.GpuAvailable,
                memoryProfile = status.MemoryProfile,
                isReady = status.IsReady,
            });
        })
        .WithName("GetAiStatus")
        .WithSummary("Returns overall AI subsystem health status.")
        .Produces<AiHealthStatus>(StatusCodes.Status200OK)
        .RequireAdmin();

        // ── GET /ai/models ───────────────────────────────────────────────────
        group.MapGet("/models", (
            IModelDownloadManager downloadManager,
            IModelLifecycleManager lifecycle,
            AiSettings settings,
            ModelInventory inventory) =>
        {
            var statuses = downloadManager.GetAllStatuses()
                .Select(status =>
                {
                    return ToModelStatusResponse(status, settings, lifecycle.CurrentlyLoadedRole, inventory);
                })
                .ToList();
            var lifecycleRoles = statuses.Select(status => status.Role).ToHashSet(StringComparer.OrdinalIgnoreCase);
            statuses.AddRange(settings.OperationalRoles.Keys
                .Where(role => !lifecycleRoles.Contains(role))
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(role => ToOperationalStatusResponse(role, settings)));
            return Results.Ok(statuses);
        })
        .WithName("GetAiModelStatuses")
        .WithSummary("Returns download and lifecycle status for all AI model roles.")
        .Produces<IReadOnlyList<AiModelStatus>>(StatusCodes.Status200OK)
        .RequireAdmin();

        // ── POST /ai/models/{role}/download ──────────────────────────────────
        group.MapPost("/models/{role}/download", async (
            string role,
            IModelDownloadManager downloadManager,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            if (!TryParseModelRole(role, out var modelRole))
                return UnknownRoleProblem(role);

            try
            {
                await downloadManager.StartDownloadAsync(modelRole, ct);
                return Results.Accepted();
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger("AiModelCommands").LogError(ex, "Could not start model download for {Role}", modelRole);
                return ModelCommandProblem("download_start_failed", "The model download could not be started.");
            }
        })
        .WithName("StartAiModelDownload")
        .WithSummary("Starts downloading the model for the specified role. Returns 202 Accepted immediately; progress is reported via SignalR.")
        .Produces(StatusCodes.Status202Accepted)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAdmin();

        // ── DELETE /ai/models/{role}/download ────────────────────────────────
        group.MapDelete("/models/{role}/download", async (
            string role,
            IModelDownloadManager downloadManager,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            if (!TryParseModelRole(role, out var modelRole))
                return UnknownRoleProblem(role);

            try
            {
                await downloadManager.CancelDownloadAsync(modelRole, ct);
                return Results.Ok(new { cancelled = true, role = ToRoleKey(modelRole) });
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger("AiModelCommands").LogError(ex, "Could not cancel model download for {Role}", modelRole);
                return ModelCommandProblem("download_cancel_failed", "The model download could not be cancelled.");
            }
        })
        .WithName("CancelAiModelDownload")
        .WithSummary("Cancels an in-progress model download for the specified role.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAdmin();

        // ── POST /ai/models/{role}/load ──────────────────────────────────────
        group.MapPost("/models/{role}/load", async (
            string role,
            IModelLifecycleManager lifecycle,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            if (!TryParseModelRole(role, out var modelRole))
                return UnknownRoleProblem(role);

            try
            {
                await lifecycle.LoadModelAsync(modelRole, ct);
                return Results.Ok(new { loaded = true, role = ToRoleKey(modelRole) });
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger("AiModelCommands").LogError(ex, "Could not load model {Role}", modelRole);
                return ModelCommandProblem("model_load_failed", "The model could not be loaded.");
            }
        })
        .WithName("LoadAiModel")
        .WithSummary("Loads the model for the specified role into memory. Unloads any currently loaded model first.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAdmin();

        // ── POST /ai/models/{role}/unload ────────────────────────────────────
        group.MapPost("/models/{role}/unload", async (
            string role,
            IModelLifecycleManager lifecycle,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            if (!TryParseModelRole(role, out var modelRole))
                return UnknownRoleProblem(role);

            try
            {
                // Only unload if the requested role is currently loaded.
                if (lifecycle.CurrentlyLoadedRole == modelRole)
                    await lifecycle.UnloadCurrentAsync(ct);

                return Results.Ok(new { unloaded = true, role = ToRoleKey(modelRole) });
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger("AiModelCommands").LogError(ex, "Could not unload model {Role}", modelRole);
                return ModelCommandProblem("model_unload_failed", "The model could not be unloaded.");
            }
        })
        .WithName("UnloadAiModel")
        .WithSummary("Unloads the model for the specified role from memory, freeing resources.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAdmin();

        // ── GET /ai/config ───────────────────────────────────────────────────
        group.MapGet("/config", (
            AiSettings settings) =>
        {
            return Results.Ok(settings);
        })
        .WithName("GetAiConfig")
        .WithSummary("Returns the current AI configuration (config/ai.json).")
        .Produces<AiSettings>(StatusCodes.Status200OK)
        .RequireAdmin();

        // ── PUT /ai/config ───────────────────────────────────────────────────
        group.MapPut("/config", (
            AiSettings settings,
            IConfigurationLoader configLoader) =>
        {
            var errors = ValidateAiSettings(settings);
            if (errors.Count > 0)
                return Results.ValidationProblem(errors.ToDictionary(e => e.Key, e => e.Value));

            configLoader.SaveAi(settings);
            return Results.Ok(new { saved = true });
        })
        .WithName("SaveAiConfig")
        .WithSummary("Saves updated AI configuration to config/ai.json.")
        .Produces(StatusCodes.Status200OK)
        .RequireAdmin();

        // ── GET /ai/profile ──────────────────────────────────────────────────
        group.MapGet("/profile", (AiSettings settings) =>
        {
            var p = settings.HardwareProfile;
            return Results.Ok(new
            {
                tier               = p.Tier,
                backend            = p.Backend,
                gpu_name           = p.GpuName,
                tokens_per_second  = p.TokensPerSecond,
                available_ram_mb   = p.AvailableRamMb,
                benchmarked_at     = p.BenchmarkedAt,
            });
        })
        .WithName("GetAiHardwareProfile")
        .WithSummary("Returns the cached hardware profile and performance tier.")
        .Produces(StatusCodes.Status200OK)
        .RequireAdmin();

        // ── POST /ai/benchmark ───────────────────────────────────────────────
        group.MapPost("/benchmark", async (
            MediaEngine.AI.Infrastructure.HardwareBenchmarkService benchmark,
            CancellationToken ct) =>
        {
            var profile = await benchmark.BenchmarkAsync(ct);
            return Results.Ok(new
            {
                tier               = profile.Tier,
                backend            = profile.Backend,
                gpu_name           = profile.GpuName,
                tokens_per_second  = profile.TokensPerSecond,
                available_ram_mb   = profile.AvailableRamMb,
                benchmarked_at     = profile.BenchmarkedAt,
            });
        })
        .WithName("RunAiHardwareBenchmark")
        .WithSummary("Re-runs the hardware benchmark and returns the updated profile.")
        .Produces(StatusCodes.Status200OK)
        .RequireAdmin();

        group.MapGet("/benchmark/suites", (
            AiBenchmarkHarness harness) =>
        {
            return Results.Ok(harness.GetBuiltInSuites().Select(suite => new
            {
                key = suite.Key,
                role = suite.OperationalRole ?? ToRoleKey(suite.Role),
                gates = suite.Gates,
                cases = suite.Cases,
            }));
        })
        .WithName("GetAiBenchmarkSuites")
        .WithSummary("Returns built-in model validation suites and promotion gates.")
        .Produces(StatusCodes.Status200OK)
        .RequireAdmin();

        group.MapPost("/benchmark/suites/{suiteKey}/run", async (
            string suiteKey,
            AiBenchmarkRunRequest request,
            AiBenchmarkHarness harness,
            IAiBenchmarkModelRunner runner,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            try
            {
                var report = await harness.RunAsync(suiteKey, request.CatalogKey, runner,
                    new(request.AllowHardwareBenchmark, request.AllowModelExecution), ct);
                return Results.Ok(report);
            }
            catch (AiBenchmarkExecutionBlockedException ex)
            {
                return Results.Problem(
                    detail: string.Join(" ", ex.BlockingReasons),
                    type: $"https://tuvima.local/problems/ai/{ex.Code}",
                    title: "AI evaluation is blocked",
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?> { ["blockingReasons"] = ex.BlockingReasons });
            }
            catch (AiBenchmarkRuntimeUnavailableException ex)
            {
                return Results.Problem(
                    detail: "The configured local runtime cannot execute this evaluation role.",
                    type: $"https://tuvima.local/problems/ai/{ex.Code}",
                    title: "AI evaluation runtime is unavailable",
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    extensions: new Dictionary<string, object?> { ["role"] = ex.Role });
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger("AiBenchmarkExecution").LogError(ex, "AI evaluation {Suite} failed", suiteKey);
                return Results.Problem(
                    detail: "The AI evaluation failed. Review Engine logs for diagnostic details.",
                    type: "https://tuvima.local/problems/ai/evaluation-failed",
                    title: "AI evaluation failed",
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("RunAiBenchmarkSuite")
        .WithSummary("Runs a versioned local text evaluation suite after explicit hardware and model-execution opt-in.")
        .Produces<AiBenchmarkReport>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status422UnprocessableEntity)
        .RequireAdmin();

        // ── GET /ai/resources ────────────────────────────────────────────────
        group.MapGet("/resources", (ResourceMonitorService monitor) =>
        {
            var snapshot = monitor.GetSnapshot();
            return Results.Ok(new
            {
                total_ram_mb       = snapshot.TotalRamMb,
                free_ram_mb        = snapshot.FreeRamMb,
                engine_ram_mb      = snapshot.EngineRamMb,
                cpu_pressure       = snapshot.CpuPressure,
                transcoding_active = snapshot.TranscodingActive,
            });
        })
        .WithName("GetAiResourceSnapshot")
        .WithSummary("Returns current system resource usage (RAM, CPU pressure, transcoding status).")
        .Produces(StatusCodes.Status200OK)
        .RequireAdmin();

        // ── GET /ai/enrichment/progress ──────────────────────────────────────
        group.MapGet("/enrichment/progress", async (
            ICanonicalValueRepository canonicals,
            CancellationToken ct) =>
        {
            // Items that have a description but not yet themes → pending enrichment.
            var pending   = await canonicals.GetEntitiesNeedingEnrichmentAsync("description", "themes", 10000, ct);
            // Items that already have themes → completed enrichment.
            var completed = await canonicals.GetEntitiesNeedingEnrichmentAsync("themes", "__nonexistent__", 10000, ct);
            int pendingCount   = pending.Count;
            int completedCount = completed.Count;
            return Results.Ok(new
            {
                pending_count   = pendingCount,
                completed_count = completedCount,
                total           = pendingCount + completedCount,
            });
        })
        .WithName("GetAiEnrichmentProgress")
        .WithSummary("Returns pending and completed AI enrichment counts.")
        .Produces(StatusCodes.Status200OK)
        .RequireAdmin();

        return group;
    }

    private static bool TryParseModelRole(string value, out AiModelRole role)
    {
        var normalized = value.Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        return Enum.TryParse(normalized, ignoreCase: true, out role);
    }

    private static string UnknownRoleMessage(string role) =>
        $"Unknown model role: '{role}'. Valid values: {string.Join(", ", Enum.GetValues<AiModelRole>().Select(ToRoleKey))}.";

    private static IResult UnknownRoleProblem(string role) => Results.Problem(
        detail: UnknownRoleMessage(role),
        type: "https://tuvima.local/problems/ai/unknown-model-role",
        title: "Unknown AI model role",
        statusCode: StatusCodes.Status400BadRequest);

    private static IResult ModelCommandProblem(string code, string detail) => Results.Problem(
        detail: detail,
        type: $"https://tuvima.local/problems/ai/{code}",
        title: "AI model operation failed",
        statusCode: StatusCodes.Status500InternalServerError);

    private static string ToRoleKey(AiModelRole role) => AiModelDefinitions.ToRoleKey(role);

    private static string GetRequiredHardwareTier(AiModelRole role) => role switch
    {
        AiModelRole.TextScholar => "high",
        AiModelRole.TextQuality or AiModelRole.TextCjk or AiModelRole.Audio => "medium",
        _ => "low",
    };

    private static string? TryGetUriHost(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : null;
    }

    private static AiModelStatusResponse ToModelStatusResponse(AiModelStatus status) =>
        new(
            Role: ToRoleKey(status.Role),
            RoleName: status.Role.ToString(),
            Supported: true,
            ModelType: status.ModelType.ToString(),
            State: status.State.ToString(),
            Description: "",
            ModelFile: status.ModelFile,
            SizeMB: status.SizeMB,
            DownloadUrlHost: null,
            DownloadProgressPercent: status.DownloadProgressPercent,
            BytesDownloaded: status.BytesDownloaded,
            TotalBytes: status.TotalBytes,
            Loaded: status.State == AiModelState.Loaded,
            Active: status.State == AiModelState.Loaded,
            MemoryFootprintMB: status.State == AiModelState.Loaded ? status.SizeMB : 0,
            RequiredHardwareTier: GetRequiredHardwareTier(status.Role),
            ErrorMessage: status.ErrorMessage,
            CatalogKey: null,
            DisplayName: "",
            Family: "",
            Provider: "",
            License: "",
            Runtime: "",
            SelectionTier: "",
            SelectionStatus: "",
            SelectionRationale: "",
            RoleRequirement: "",
            BenchmarkSuite: "",
            ValidationWarnings: [],
            Capabilities: [],
            DiskStatus: "unknown",
            DiskSizeMB: 0,
            MemoryEnvelopeMB: 0,
            Quantization: "",
            SourceUrl: "",
            ChecksumStatus: "unknown",
            ConfigurationReady: false,
            RuntimeReady: false,
            Validated: false,
            CanOperate: false,
            Experimental: false,
            BlockingReasons: []);

    private static AiModelStatusResponse ToModelStatusResponse(AiModelStatus status, AiSettings settings, AiModelRole? currentRole, ModelInventory inventory)
    {
        var definition = settings.Models.GetByRole(status.Role);
        var advisor = new AiModelSelectionAdvisor(settings);
        var decision = advisor.GetDecision(status.Role);
        var catalog = settings.GetCatalogEntryForRole(status.Role);
        var isLoaded = currentRole == status.Role || status.State == AiModelState.Loaded;
        return new AiModelStatusResponse(
            Role: ToRoleKey(status.Role),
            RoleName: status.Role.ToString(),
            Supported: true,
            ModelType: status.ModelType.ToString(),
            State: status.State.ToString(),
            Description: definition.Description,
            ModelFile: status.ModelFile,
            SizeMB: status.SizeMB,
            DownloadUrlHost: TryGetUriHost(definition.DownloadUrl),
            DownloadProgressPercent: status.DownloadProgressPercent,
            BytesDownloaded: status.BytesDownloaded,
            TotalBytes: status.TotalBytes,
            Loaded: isLoaded,
            Active: currentRole == status.Role,
            MemoryFootprintMB: isLoaded ? definition.SizeMB : 0,
            RequiredHardwareTier: GetRequiredHardwareTier(status.Role),
            ErrorMessage: status.ErrorMessage,
            CatalogKey: definition.CatalogKey,
            DisplayName: decision.DisplayName,
            Family: decision.Family,
            Provider: decision.Provider,
            License: decision.License,
            Runtime: decision.Runtime,
            SelectionTier: decision.SelectionTier,
            SelectionStatus: decision.Status,
            SelectionRationale: decision.Rationale,
            RoleRequirement: decision.Requirement,
            BenchmarkSuite: decision.BenchmarkSuite,
            ValidationWarnings: decision.Warnings,
            Capabilities: FormatCapabilities(catalog?.Capabilities),
            DiskStatus: GetDiskStatus(inventory.GetModelPath(status.Role)),
            DiskSizeMB: GetDiskSizeMB(inventory.GetModelPath(status.Role)),
            MemoryEnvelopeMB: decision.MemoryEnvelopeMB,
            Quantization: decision.Quantization,
            SourceUrl: decision.SourceUrl,
            ChecksumStatus: decision.ChecksumConfigured ? "configured" : "missing",
            ConfigurationReady: decision.ConfigurationReady,
            RuntimeReady: decision.RuntimeReady,
            Validated: decision.Validated,
            CanOperate: decision.CanEnable,
            Experimental: decision.Experimental,
            BlockingReasons: decision.BlockingReasons);
    }

    private static AiModelStatusResponse ToOperationalStatusResponse(string role, AiSettings settings)
    {
        var advisor = new AiModelSelectionAdvisor(settings);
        var decision = advisor.GetDecision(role);
        settings.ModelCatalog.TryGetValue(decision.CatalogKey ?? "", out var catalog);
        settings.OperationalRoles.TryGetValue(role, out var definition);
        var file = catalog?.File ?? "";
        var path = GetCatalogModelPath(settings, file, definition?.RuntimeKind);
        var state = path is null ? "Unavailable" : GetDiskStatus(path) == "present" ? "Ready" : "NotDownloaded";
        return new(
            role, role.Replace('_', ' '), false, definition?.RuntimeKind ?? "unknown", state,
            decision.Requirement, file, decision.SizeMB, TryGetUriHost(catalog?.DownloadUrl), 0, 0, 0,
            false, false, 0, "not integrated", null, decision.CatalogKey, decision.DisplayName,
            decision.Family, decision.Provider, decision.License, decision.Runtime, decision.SelectionTier,
            decision.Status, decision.Rationale, decision.Requirement, decision.BenchmarkSuite, decision.Warnings,
            FormatCapabilities(catalog?.Capabilities), path is null ? "not_configured" : GetDiskStatus(path),
            path is null ? 0 : GetDiskSizeMB(path), decision.MemoryEnvelopeMB, decision.Quantization,
            decision.SourceUrl, decision.ChecksumConfigured ? "configured" : "missing",
            decision.ConfigurationReady, decision.RuntimeReady, decision.Validated, decision.CanEnable,
            decision.Experimental, decision.BlockingReasons);
    }

    private static string? GetCatalogModelPath(AiSettings settings, string file, string? runtimeKind)
    {
        if (string.IsNullOrWhiteSpace(file)) return null;
        var subdirectory = runtimeKind?.ToLowerInvariant() switch
        {
            "audio" => "whisper",
            "text" => "llama",
            _ => null,
        };
        return subdirectory is null ? null : Path.Combine(settings.ModelsDirectory, subdirectory, file);
    }

    private static string GetDiskStatus(string path) => File.Exists(path) ? "present" : "missing";

    private static long GetDiskSizeMB(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? (long)Math.Ceiling(info.Length / 1024d / 1024d) : 0;
    }

    private static IReadOnlyList<string> FormatCapabilities(AiModelCapabilities? capabilities)
    {
        if (capabilities is null)
            return [];

        var values = new List<string>();
        if (capabilities.TextInput) values.Add("text input");
        if (capabilities.AudioInput) values.Add("audio input");
        if (capabilities.ImageInput) values.Add("image input");
        if (capabilities.TextOutput) values.Add("text output");
        if (capabilities.StructuredJson) values.Add("structured JSON");
        if (capabilities.Gbnf) values.Add("GBNF");
        if (capabilities.TimestampSegments) values.Add("segment timestamps");
        if (capabilities.WordTimestamps) values.Add("word timestamps");
        if (capabilities.SyncGrade) values.Add("sync-grade");
        if (capabilities.Multilingual) values.Add("multilingual");
        if (capabilities.Cjk) values.Add("CJK");
        if (capabilities.ExperimentalMultimodal) values.Add("experimental multimodal");
        if (capabilities.EmbeddingOutput) values.Add("embeddings");
        if (capabilities.FunctionCalling) values.Add("function calling");
        if (capabilities.ToolCalling) values.Add("tool calling");
        return values;
    }

    private static IReadOnlyDictionary<string, string[]> ValidateAiSettings(AiSettings settings)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        void Add(string key, string message) => errors[key] = [message];

        foreach (var error in AiSettingsValidator.Validate(settings))
            Add(error.Path, error.Message);

        if (string.IsNullOrWhiteSpace(settings.ModelsDirectory))
            Add("models_directory", "Models directory is required.");
        if (settings.IdleUnloadSeconds <= 0)
            Add("idle_unload_seconds", "Idle unload seconds must be positive.");
        if (settings.InferenceTimeoutSeconds <= 0)
            Add("inference_timeout_seconds", "Inference timeout seconds must be positive.");
        if (settings.EnrichmentBatchSize <= 0)
            Add("enrichment_batch_size", "Enrichment batch size must be positive.");

        foreach (var role in Enum.GetValues<AiModelRole>())
        {
            var key = ToRoleKey(role);
            var model = settings.Models.GetByRole(role);
            if (string.IsNullOrWhiteSpace(model.File))
                Add($"models.{key}.file", "Model file is required.");
            if (model.SizeMB <= 0)
                Add($"models.{key}.size_mb", "Model size must be positive.");
            if (model.ContextLength <= 0)
                Add($"models.{key}.context_length", "Context length must be positive.");
            if (model.MaxTokens <= 0)
                Add($"models.{key}.max_tokens", "Max tokens must be positive.");
            if (model.Temperature < 0 || model.Temperature > 2)
                Add($"models.{key}.temperature", "Temperature must be between 0 and 2.");
            if (model.GpuLayers < 0)
                Add($"models.{key}.gpu_layers", "GPU layers cannot be negative.");
            if (model.Threads <= 0)
                Add($"models.{key}.threads", "Threads must be positive.");
            if (!string.IsNullOrWhiteSpace(model.DownloadUrl)
                && !Uri.TryCreate(model.DownloadUrl, UriKind.Absolute, out _))
            {
                Add($"models.{key}.download_url", "Download URL must be an absolute URI.");
            }
            if (!string.IsNullOrWhiteSpace(model.CatalogKey)
                && !settings.ModelCatalog.ContainsKey(model.CatalogKey))
            {
                Add($"models.{key}.catalog_key", "Catalog key must reference model_catalog.");
            }
        }

        foreach (var (key, entry) in settings.ModelCatalog)
        {
            if (string.IsNullOrWhiteSpace(key))
                Add("model_catalog", "Model catalog keys cannot be empty.");
            if (string.IsNullOrWhiteSpace(entry.DisplayName))
                Add($"model_catalog.{key}.display_name", "Display name is required.");
            if (entry.SizeMB <= 0)
                Add($"model_catalog.{key}.size_mb", "Catalog model size must be positive.");
            if (!string.IsNullOrWhiteSpace(entry.DownloadUrl)
                && !Uri.TryCreate(entry.DownloadUrl, UriKind.Absolute, out _))
            {
                Add($"model_catalog.{key}.download_url", "Download URL must be an absolute URI.");
            }
            if (!string.IsNullOrWhiteSpace(entry.SourceUrl)
                && !Uri.TryCreate(entry.SourceUrl, UriKind.Absolute, out _))
            {
                Add($"model_catalog.{key}.source_url", "Source URL must be an absolute URI.");
            }
        }

        foreach (var (key, requirement) in settings.RoleRequirements)
        {
            if (requirement.PreferredCatalogKeys.Count == 0)
                Add($"role_requirements.{key}.preferred_catalog_keys", "At least one preferred model is required.");

            foreach (var catalogKey in requirement.PreferredCatalogKeys.Concat(requirement.FallbackCatalogKeys))
            {
                if (!settings.ModelCatalog.ContainsKey(catalogKey))
                    Add($"role_requirements.{key}.{catalogKey}", "Requirement references an unknown model catalog key.");
            }
        }

        var advisor = new AiModelSelectionAdvisor(settings);
        foreach (var (key, role) in settings.OperationalRoles.Where(pair => pair.Value.Enabled))
        {
            var decision = advisor.GetDecision(key);
            if (!decision.CanEnable)
                Add($"operational_roles.{key}.enabled", $"Cannot enable this role: {string.Join(" ", decision.Warnings)}");
            if (role.Experimental)
            {
                if (!settings.RoleRequirements.TryGetValue(key, out var experimentalRequirement))
                    Add($"operational_roles.{key}", "Experimental roles require an explicit role requirement.");
                else if (!experimentalRequirement.ExperimentalAllowed)
                    Add($"operational_roles.{key}.experimental", "This role does not permit experimental models.");
            }
        }

        if (settings.Scheduling.WhisperBakeWindowHours <= 0)
            Add("scheduling.whisper_bake_window_hours", "Whisper bake window must be positive.");

        return errors;
    }

    private sealed record AiModelStatusResponse(
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

    private sealed record AiBenchmarkRunRequest(
        string CatalogKey,
        bool AllowHardwareBenchmark,
        bool AllowModelExecution);
}
