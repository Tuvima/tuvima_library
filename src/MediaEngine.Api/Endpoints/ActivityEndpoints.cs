using System.Text.Json.Serialization;
using MediaEngine.Api.Security;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Endpoints;

public static class ActivityEndpoints
{
    public static IEndpointRouteBuilder MapActivityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/activity")
            .WithTags("Activity");

        // GET /activity/recent?limit=50 — returns the most recent activity entries.
        group.MapGet("/recent", async (
            ISystemActivityRepository repo,
            int? limit) =>
        {
            var entries = await repo.GetRecentAsync(limit ?? 50);

            var response = entries.Select(MapEntry).ToList();

            return Results.Ok(response);
        })
        .WithName("GetRecentActivity")
        .WithSummary("Returns the most recent activity log entries, newest first.")
        .Produces<List<ActivityEntryResponse>>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // POST /activity/prune — manual prune trigger.
        group.MapPost("/prune", async (
            ISystemActivityRepository repo,
            IConfigurationLoader      configLoader) =>
        {
            var maintenance = configLoader.LoadMaintenance();
            var retentionDays = maintenance.ActivityRetentionDays;
            var deleted = await repo.PruneOlderThanAsync(retentionDays);

            return Results.Ok(new PruneResponse
            {
                Deleted       = deleted,
                RetentionDays = retentionDays,
            });
        })
        .WithName("PruneActivity")
        .WithSummary("Deletes activity entries older than the configured retention period.")
        .Produces<PruneResponse>(StatusCodes.Status200OK)
        .RequireAdmin();

        // GET /activity/stats — entry count and retention setting.
        group.MapGet("/stats", async (
            ISystemActivityRepository repo,
            IConfigurationLoader      configLoader) =>
        {
            var maintenance = configLoader.LoadMaintenance();
            var count       = await repo.CountAsync();

            return Results.Ok(new ActivityStatsResponse
            {
                TotalEntries  = count,
                RetentionDays = maintenance.ActivityRetentionDays,
            });
        })
        .WithName("GetActivityStats")
        .WithSummary("Returns the total entry count and the current retention setting.")
        .Produces<ActivityStatsResponse>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // PUT /activity/retention — update retention period.
        group.MapPut("/retention", (
            int days,
            IConfigurationLoader configLoader) =>
        {
            if (days < 1 || days > 365)
                return Results.BadRequest("Retention must be between 1 and 365 days.");

            var maintenance = configLoader.LoadMaintenance();
            maintenance.ActivityRetentionDays = days;
            configLoader.SaveMaintenance(maintenance);

            return Results.Ok(new ActivityStatsResponse
            {
                TotalEntries  = 0,
                RetentionDays = days,
            });
        })
        .WithName("UpdateActivityRetention")
        .WithSummary("Updates the activity retention period in days.")
        .Produces<ActivityStatsResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAdmin();

        // GET /activity/by-types?types=BatchCreated,BatchCompleted&limit=50
        // Returns recent entries filtered by action types — used by Timeline view.
        group.MapGet("/by-types", async (
            ISystemActivityRepository repo,
            string? types,
            int? limit) =>
        {
            var typeList = string.IsNullOrWhiteSpace(types)
                ? Array.Empty<string>()
                : types.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var entries = await repo.GetRecentByTypesAsync(typeList, limit ?? 50);
            var response = entries.Select(MapEntry).ToList();
            return Results.Ok(response);
        })
        .WithName("GetActivityByTypes")
        .WithSummary("Returns recent activity entries filtered by one or more action types (comma-separated).")
        .Produces<List<ActivityEntryResponse>>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // GET /activity/run/{runId} — returns all entries for a specific ingestion run.
        group.MapGet("/run/{runId:guid}", async (
            ISystemActivityRepository repo,
            Guid runId) =>
        {
            var entries = await repo.GetByRunIdAsync(runId);
            var response = entries.Select(MapEntry).ToList();
            return Results.Ok(response);
        })
        .WithName("GetActivityByRunId")
        .WithSummary("Returns all activity entries for a given ingestion run, ordered by timestamp.")
        .Produces<List<ActivityEntryResponse>>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        return app;
    }

    private static ActivityEntryResponse MapEntry(SystemActivityEntry e) => new()
    {
        Id              = e.Id,
        OccurredAt      = e.OccurredAt.ToString("O"),
        ActionType      = e.ActionType,
        CollectionName         = e.CollectionName,
        EntityId        = e.EntityId?.ToString(),
        EntityType      = e.EntityType,
        ProfileId       = e.ProfileId?.ToString(),
        ChangesJson     = e.ChangesJson,
        Detail          = e.Detail,
        IngestionRunId  = e.IngestionRunId?.ToString(),
    };
}

// ── Response DTOs ────────────────────────────────────────────────────────────

public sealed class ActivityEntryResponse
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("occurred_at")]
    public string OccurredAt { get; init; } = string.Empty;

    [JsonPropertyName("action_type")]
    public string ActionType { get; init; } = string.Empty;

    [JsonPropertyName("collection_name")]
    public string? CollectionName { get; init; }

    [JsonPropertyName("entity_id")]
    public string? EntityId { get; init; }

    [JsonPropertyName("entity_type")]
    public string? EntityType { get; init; }

    [JsonPropertyName("profile_id")]
    public string? ProfileId { get; init; }

    [JsonPropertyName("changes_json")]
    public string? ChangesJson { get; init; }

    [JsonPropertyName("detail")]
    public string? Detail { get; init; }

    [JsonPropertyName("ingestion_run_id")]
    public string? IngestionRunId { get; init; }
}

public sealed class PruneResponse
{
    [JsonPropertyName("deleted")]
    public int Deleted { get; init; }

    [JsonPropertyName("retention_days")]
    public int RetentionDays { get; init; }
}

public sealed class ActivityStatsResponse
{
    [JsonPropertyName("total_entries")]
    public long TotalEntries { get; init; }

    [JsonPropertyName("retention_days")]
    public int RetentionDays { get; init; }
}
