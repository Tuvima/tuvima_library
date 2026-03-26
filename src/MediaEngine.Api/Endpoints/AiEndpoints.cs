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
            return Results.Ok(status);
        })
        .WithName("GetAiStatus")
        .WithSummary("Returns overall AI subsystem health status.")
        .Produces<AiHealthStatus>(StatusCodes.Status200OK)
        .RequireAdmin();

        // ── GET /ai/models ───────────────────────────────────────────────────
        group.MapGet("/models", (
            IModelDownloadManager downloadManager) =>
        {
            var statuses = downloadManager.GetAllStatuses();
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
            if (!Enum.TryParse<AiModelRole>(role, ignoreCase: true, out var modelRole))
                return Results.BadRequest($"Unknown model role: '{role}'. Valid values: TextFast, TextQuality, Audio.");

            await downloadManager.StartDownloadAsync(modelRole, ct);
            return Results.Accepted();
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
            if (!Enum.TryParse<AiModelRole>(role, ignoreCase: true, out var modelRole))
                return Results.BadRequest($"Unknown model role: '{role}'. Valid values: TextFast, TextQuality, Audio.");

            await downloadManager.CancelDownloadAsync(modelRole, ct);
            return Results.Ok(new { cancelled = true, role });
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
            if (!Enum.TryParse<AiModelRole>(role, ignoreCase: true, out var modelRole))
                return Results.BadRequest($"Unknown model role: '{role}'. Valid values: TextFast, TextQuality, Audio.");

            await lifecycle.LoadModelAsync(modelRole, ct);
            return Results.Ok(new { loaded = true, role });
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
            if (!Enum.TryParse<AiModelRole>(role, ignoreCase: true, out var modelRole))
                return Results.BadRequest($"Unknown model role: '{role}'. Valid values: TextFast, TextQuality, Audio.");

            // Only unload if the requested role is currently loaded.
            if (lifecycle.CurrentlyLoadedRole == modelRole)
                await lifecycle.UnloadCurrentAsync(ct);

            return Results.Ok(new { unloaded = true, role });
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
}
