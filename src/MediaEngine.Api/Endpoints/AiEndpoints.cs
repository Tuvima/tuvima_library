using MediaEngine.AI.Configuration;
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

        return group;
    }
}
