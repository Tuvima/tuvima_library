using System.Reflection;
using System.Text.Json.Serialization;
using MediaEngine.Api.Models;
using MediaEngine.Api.Security;
using MediaEngine.Api.Services;
using MediaEngine.Api.Services.ReadServices;
using MediaEngine.Ingestion.Contracts;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Endpoints;

public static class SystemEndpoints
{
    // Version sourced from the assembly at startup — no hard-coded string to forget to bump.
    private static readonly string AppVersion =
        typeof(SystemEndpoints).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?.Split('+')[0]           // strip build metadata (e.g. git hash)
        ?? "1.0.0";

    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        // No auth required — allows external apps to verify the URL is reachable.
        // The X-Api-Key middleware validates the key if one is supplied, returning
        // 401 for invalid keys; absent keys pass through to this endpoint.
        app.MapGet("/system/status", (IConfigurationLoader configLoader) =>
        {
            var core = configLoader.LoadCore();
            return Results.Ok(new SystemStatusResponse
            {
                Status = "ok",
                Version = AppVersion,
                Language = core?.Language.Metadata ?? "en",
            });
        })
        .WithTags("System")
        .WithName("GetSystemStatus")
        .WithSummary("Returns service health and version. Used by external apps to test connectivity.")
        .Produces<SystemStatusResponse>(StatusCodes.Status200OK);

        app.MapGet("/system/activity-status", async (
            IMediaOperationRepository operations,
            CancellationToken ct) =>
        {
            var queue = await operations.GetQueueAsync(null, 200, ct);
            var active = queue
                .Where(operation => operation.Status.Equals(MediaOperationStatus.Leased, StringComparison.OrdinalIgnoreCase)
                                    || operation.Status.Equals(MediaOperationStatus.Running, StringComparison.OrdinalIgnoreCase))
                .OrderBy(operation => operation.Priority)
                .ThenByDescending(operation => operation.UpdatedAt)
                .Take(50)
                .Select(SystemActivityOperationDto.From)
                .ToList();

            return Results.Ok(active);
        })
        .WithTags("System")
        .WithName("GetSystemActivityStatus")
        .WithSummary("Returns sanitized active Engine operations for the Dashboard activity indicator.")
        .Produces<List<SystemActivityOperationDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        app.MapGet("/system/watcher-status", (IFileWatcher watcher) =>
            Results.Ok(new
            {
                running = watcher.IsRunning,
                directory_count = watcher.WatchedPaths.Count,
                directories = watcher.WatchedPaths,
                event_count = watcher.EventCount,
                last_event_at = watcher.LastEventAt,
                error_count = watcher.ErrorCount,
                last_error_at = watcher.LastErrorAt,
                last_error_kind = watcher.LastErrorKind,
                last_error_message = watcher.LastErrorMessage,
            }))
        .WithTags("System")
        .WithName("GetWatcherStatus")
        .WithSummary("Returns file watcher diagnostic status.")
        .RequireAdmin();

        app.MapPost("/maintenance/sweep-orphan-assets", (
            AssetStoreCleanupService cleanupService,
            CancellationToken ct) =>
        {
            var result = cleanupService.SweepOrphanAssets(ct);
            return Results.Ok(new
            {
                cleaned = result.Cleaned,
                message = result.Message,
            });
        })
        .WithTags("System")
        .WithName("SweepOrphanAssets")
        .WithSummary("Scans .data/assets for managed files not referenced by the database and removes them.")
        .Produces(StatusCodes.Status200OK)
        .RequireAdmin();

        return app;
    }
}

public sealed record SystemActivityOperationDto
{
    [JsonPropertyName("id")] public Guid Id { get; init; }
    [JsonPropertyName("operation_type")] public string OperationType { get; init; } = string.Empty;
    [JsonPropertyName("operation_kind")] public string OperationKind { get; init; } = string.Empty;
    [JsonPropertyName("status")] public string Status { get; init; } = string.Empty;
    [JsonPropertyName("stage")] public string? Stage { get; init; }
    [JsonPropertyName("progress_percent")] public int ProgressPercent { get; init; }
    [JsonPropertyName("items_total")] public int ItemsTotal { get; init; }
    [JsonPropertyName("items_completed")] public int ItemsCompleted { get; init; }
    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; init; }

    public static SystemActivityOperationDto From(MediaOperation operation) => new()
    {
        Id = operation.Id,
        OperationType = operation.OperationType,
        OperationKind = operation.OperationKind,
        Status = operation.Status,
        Stage = operation.Stage,
        ProgressPercent = operation.ProgressPercent,
        ItemsTotal = operation.ItemsTotal,
        ItemsCompleted = operation.ItemsCompleted,
        UpdatedAt = operation.UpdatedAt,
    };
}
