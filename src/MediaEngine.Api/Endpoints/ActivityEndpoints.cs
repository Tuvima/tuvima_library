using System.Text.Json;
using System.Text.Json.Serialization;
using MediaEngine.Api.Security;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
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
            IPersonRepository personRepo,
            IQidLabelRepository qidLabelRepo,
            int? limit,
            CancellationToken ct) =>
        {
            var entries = await repo.GetRecentAsync(limit ?? 50);

            var response = await MapEntriesAsync(entries, personRepo, qidLabelRepo, ct);

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
            IPersonRepository personRepo,
            IQidLabelRepository qidLabelRepo,
            string? types,
            int? limit,
            CancellationToken ct) =>
        {
            var typeList = string.IsNullOrWhiteSpace(types)
                ? Array.Empty<string>()
                : types.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var entries = await repo.GetRecentByTypesAsync(typeList, limit ?? 50);
            var response = await MapEntriesAsync(entries, personRepo, qidLabelRepo, ct);
            return Results.Ok(response);
        })
        .WithName("GetActivityByTypes")
        .WithSummary("Returns recent activity entries filtered by one or more action types (comma-separated).")
        .Produces<List<ActivityEntryResponse>>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // GET /activity/run/{runId} — returns all entries for a specific ingestion run.
        group.MapGet("/run/{runId:guid}", async (
            ISystemActivityRepository repo,
            IPersonRepository personRepo,
            IQidLabelRepository qidLabelRepo,
            Guid runId,
            CancellationToken ct) =>
        {
            var entries = await repo.GetByRunIdAsync(runId);
            var response = await MapEntriesAsync(entries, personRepo, qidLabelRepo, ct);
            return Results.Ok(response);
        })
        .WithName("GetActivityByRunId")
        .WithSummary("Returns all activity entries for a given ingestion run, ordered by timestamp.")
        .Produces<List<ActivityEntryResponse>>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        return app;
    }

    private static async Task<List<ActivityEntryResponse>> MapEntriesAsync(
        IReadOnlyList<SystemActivityEntry> entries,
        IPersonRepository personRepo,
        IQidLabelRepository qidLabelRepo,
        CancellationToken ct)
    {
        var response = new List<ActivityEntryResponse>(entries.Count);
        foreach (var entry in entries)
        {
            response.Add(await MapEntryAsync(entry, personRepo, qidLabelRepo, ct));
        }

        return response;
    }

    private static async Task<ActivityEntryResponse> MapEntryAsync(
        SystemActivityEntry e,
        IPersonRepository personRepo,
        IQidLabelRepository qidLabelRepo,
        CancellationToken ct)
    {
        var collectionName = e.CollectionName;
        var detail = e.Detail;

        if (string.Equals(e.ActionType, SystemActionType.PersonHydrated, StringComparison.OrdinalIgnoreCase)
            && NeedsPersonNameResolution(collectionName, detail))
        {
            var qid = ExtractPersonQid(e.ChangesJson) ?? ExtractFirstQid(detail);
            var resolvedName = await ResolveActivityPersonNameAsync(e.EntityId, qid, personRepo, qidLabelRepo, ct);
            if (!string.IsNullOrWhiteSpace(resolvedName))
            {
                collectionName = resolvedName;
                detail = $"Person \"{resolvedName}\" enriched from Wikidata";
            }
            else if (!string.IsNullOrWhiteSpace(qid))
            {
                collectionName = $"Name pending ({qid})";
                detail = $"Person \"Name pending ({qid})\" enriched from Wikidata";
            }
        }

        return new ActivityEntryResponse
        {
            Id              = e.Id,
            OccurredAt      = e.OccurredAt.ToString("O"),
            ActionType      = e.ActionType,
            CollectionName  = collectionName,
            EntityId        = e.EntityId?.ToString(),
            EntityType      = e.EntityType,
            ProfileId       = e.ProfileId?.ToString(),
            ChangesJson     = e.ChangesJson,
            Detail          = detail,
            IngestionRunId  = e.IngestionRunId?.ToString(),
        };
    }

    private static async Task<string?> ResolveActivityPersonNameAsync(
        Guid? personId,
        string? qid,
        IPersonRepository personRepo,
        IQidLabelRepository qidLabelRepo,
        CancellationToken ct)
    {
        if (personId.HasValue)
        {
            var person = await personRepo.FindByIdAsync(personId.Value, ct);
            var personName = ResolveBestPersonName(person?.Name);
            if (!string.IsNullOrWhiteSpace(personName))
                return personName;
        }

        if (!string.IsNullOrWhiteSpace(qid))
        {
            var label = await qidLabelRepo.GetLabelAsync(qid, ct);
            var labelName = ResolveBestPersonName(label);
            if (!string.IsNullOrWhiteSpace(labelName))
                return labelName;
        }

        return null;
    }

    private static bool NeedsPersonNameResolution(string? collectionName, string? detail) =>
        IsPlaceholderPersonName(collectionName)
        || (!string.IsNullOrWhiteSpace(detail)
            && detail.Contains("Unknown Person (", StringComparison.OrdinalIgnoreCase));

    private static string? ResolveBestPersonName(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            var trimmed = candidate.Trim();
            if (!IsPlaceholderPersonName(trimmed))
                return trimmed;
        }

        return null;
    }

    private static bool IsPlaceholderPersonName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        var trimmed = value.Trim();
        if (trimmed.StartsWith("Unknown Person (", StringComparison.OrdinalIgnoreCase))
            return true;

        return trimmed.Length > 1
            && trimmed[0] is 'Q'
            && trimmed.Skip(1).All(char.IsDigit);
    }

    private static string? ExtractPersonQid(string? changesJson)
    {
        if (string.IsNullOrWhiteSpace(changesJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(changesJson);
            return doc.RootElement.TryGetProperty("qid", out var qidElement)
                ? NormalizeQid(qidElement.GetString())
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractFirstQid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var index = value.IndexOf('Q');
        while (index >= 0 && index < value.Length - 1)
        {
            var end = index + 1;
            while (end < value.Length && char.IsDigit(value[end]))
                end++;

            if (end > index + 1)
                return value[index..end];

            index = value.IndexOf('Q', index + 1);
        }

        return null;
    }

    private static string? NormalizeQid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var qid = value.Trim();
        var slash = qid.LastIndexOf('/');
        if (slash >= 0)
            qid = qid[(slash + 1)..];

        return qid.Length > 1 && qid[0] is 'Q' && qid.Skip(1).All(char.IsDigit)
            ? qid
            : null;
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
