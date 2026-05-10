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
            IConfigurationLoader configLoader) =>
        {
            var settings = configLoader.LoadAi<AiSettings>() ?? new AiSettings();
            var statuses = downloadManager.GetAllStatuses()
                .Select(status =>
                {
                    return ToModelStatusResponse(status, settings, lifecycle.CurrentlyLoadedRole);
                })
                .ToList();
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
            CancellationToken ct) =>
        {
            if (!TryParseModelRole(role, out var modelRole))
                return Results.BadRequest(UnknownRoleMessage(role));

            try
            {
                await downloadManager.StartDownloadAsync(modelRole, ct);
                return Results.Accepted();
            }
            catch (Exception ex)
            {
                return Results.Problem($"Could not start model download for {ToRoleKey(modelRole)}: {ex.Message}", statusCode: StatusCodes.Status500InternalServerError);
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
            CancellationToken ct) =>
        {
            if (!TryParseModelRole(role, out var modelRole))
                return Results.BadRequest(UnknownRoleMessage(role));

            try
            {
                await downloadManager.CancelDownloadAsync(modelRole, ct);
                return Results.Ok(new { cancelled = true, role = ToRoleKey(modelRole) });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Could not cancel model download for {ToRoleKey(modelRole)}: {ex.Message}", statusCode: StatusCodes.Status500InternalServerError);
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
            CancellationToken ct) =>
        {
            if (!TryParseModelRole(role, out var modelRole))
                return Results.BadRequest(UnknownRoleMessage(role));

            try
            {
                await lifecycle.LoadModelAsync(modelRole, ct);
                return Results.Ok(new { loaded = true, role = ToRoleKey(modelRole) });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Could not load model {ToRoleKey(modelRole)}: {ex.Message}", statusCode: StatusCodes.Status500InternalServerError);
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
            CancellationToken ct) =>
        {
            if (!TryParseModelRole(role, out var modelRole))
                return Results.BadRequest(UnknownRoleMessage(role));

            try
            {
                // Only unload if the requested role is currently loaded.
                if (lifecycle.CurrentlyLoadedRole == modelRole)
                    await lifecycle.UnloadCurrentAsync(ct);

                return Results.Ok(new { unloaded = true, role = ToRoleKey(modelRole) });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Could not unload model {ToRoleKey(modelRole)}: {ex.Message}", statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("UnloadAiModel")
        .WithSummary("Unloads the model for the specified role from memory, freeing resources.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAdmin();

        // ── GET /ai/config ───────────────────────────────────────────────────
        group.MapGet("/config", (
            IConfigurationLoader configLoader) =>
        {
            var settings = configLoader.LoadAi<AiSettings>() ?? new AiSettings();
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

    private static string ToRoleKey(AiModelRole role) => role switch
    {
        AiModelRole.TextFast => "text_fast",
        AiModelRole.TextQuality => "text_quality",
        AiModelRole.TextScholar => "text_scholar",
        AiModelRole.Audio => "audio",
        AiModelRole.TextCjk => "text_cjk",
        _ => role.ToString(),
    };

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
            ErrorMessage: status.ErrorMessage);

    private static AiModelStatusResponse ToModelStatusResponse(AiModelStatus status, AiSettings settings, AiModelRole? currentRole)
    {
        var definition = settings.Models.GetByRole(status.Role);
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
            ErrorMessage: status.ErrorMessage);
    }

    private static IReadOnlyDictionary<string, string[]> ValidateAiSettings(AiSettings settings)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        void Add(string key, string message) => errors[key] = [message];

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
        string? ErrorMessage);
}
