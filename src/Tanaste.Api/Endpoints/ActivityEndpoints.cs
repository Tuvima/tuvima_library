using System.Text.Json.Serialization;
using Tanaste.Api.Security;
using Tanaste.Domain.Contracts;
using Tanaste.Domain.Entities;
using Tanaste.Storage.Contracts;

namespace Tanaste.Api.Endpoints;

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

            var response = entries.Select(e => new ActivityEntryResponse
            {
                Id          = e.Id,
                OccurredAt  = e.OccurredAt.ToString("O"),
                ActionType  = e.ActionType,
                HubName     = e.HubName,
                EntityId    = e.EntityId?.ToString(),
                EntityType  = e.EntityType,
                ProfileId   = e.ProfileId?.ToString(),
                ChangesJson = e.ChangesJson,
                Detail      = e.Detail,
            }).ToList();

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

        return app;
    }
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

    [JsonPropertyName("hub_name")]
    public string? HubName { get; init; }

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
