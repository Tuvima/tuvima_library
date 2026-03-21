using System.Text.Json;
using System.Text.Json.Serialization;
using MediaEngine.Api.Security;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Api.Endpoints;

public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/reports")
            .WithTags("Reports");

        // POST /reports — submit a problem report on a media item.
        group.MapPost("/", async (
            SubmitReportRequest request,
            ISystemActivityRepository activityRepo) =>
        {
            if (request.EntityId == Guid.Empty)
                return Results.BadRequest("entity_id is required.");

            var changesJson = JsonSerializer.Serialize(new
            {
                category = request.Category ?? "Other",
                note = request.Note ?? "",
                reporter = request.ReporterName ?? "Anonymous",
            });

            var entry = new SystemActivityEntry
            {
                ActionType = SystemActionType.UserReportSubmitted,
                EntityId = request.EntityId,
                EntityType = "MediaAsset",
                HubName = request.ItemTitle,
                Detail = $"Problem reported: {request.Category ?? "Other"} — {request.Note ?? "(no details)"}",
                ChangesJson = changesJson,
            };

            await activityRepo.LogAsync(entry);

            return Results.Ok(new SubmitReportResponse
            {
                Success = true,
                Message = "Report submitted successfully.",
            });
        })
        .WithName("SubmitReport")
        .WithSummary("Submits a user problem report on a media item.")
        .Produces<SubmitReportResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAdminOrCurator();

        // GET /reports/entity/{entityId} — get all reports for a specific item.
        group.MapGet("/entity/{entityId:guid}", async (
            Guid entityId,
            ISystemActivityRepository activityRepo) =>
        {
            var types = new[] { SystemActionType.UserReportSubmitted };
            var entries = await activityRepo.GetRecentByTypesAsync(types, 100);

            var reports = entries
                .Where(e => e.EntityId == entityId)
                .Select(e => new ReportEntryResponse
                {
                    Id = e.Id,
                    OccurredAt = e.OccurredAt.ToString("O"),
                    Category = ExtractJsonField(e.ChangesJson, "category") ?? "Other",
                    Note = ExtractJsonField(e.ChangesJson, "note") ?? "",
                    ReporterName = ExtractJsonField(e.ChangesJson, "reporter") ?? "Anonymous",
                    Detail = e.Detail,
                })
                .ToList();

            return Results.Ok(reports);
        })
        .WithName("GetReportsForEntity")
        .WithSummary("Returns all problem reports for a specific media item.")
        .Produces<List<ReportEntryResponse>>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // POST /reports/{activityId}/resolve — resolve a report.
        group.MapPost("/{activityId:long}/resolve", async (
            long activityId,
            ISystemActivityRepository activityRepo) =>
        {
            var entry = new SystemActivityEntry
            {
                ActionType = SystemActionType.UserReportResolved,
                Detail = $"Report #{activityId} resolved by curator.",
            };
            await activityRepo.LogAsync(entry);

            return Results.Ok(new SubmitReportResponse
            {
                Success = true,
                Message = "Report resolved.",
            });
        })
        .WithName("ResolveReport")
        .WithSummary("Marks a problem report as resolved.")
        .Produces<SubmitReportResponse>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // POST /reports/{activityId}/dismiss — dismiss a report.
        group.MapPost("/{activityId:long}/dismiss", async (
            long activityId,
            ISystemActivityRepository activityRepo) =>
        {
            var entry = new SystemActivityEntry
            {
                ActionType = SystemActionType.UserReportDismissed,
                Detail = $"Report #{activityId} dismissed by curator.",
            };
            await activityRepo.LogAsync(entry);

            return Results.Ok(new SubmitReportResponse
            {
                Success = true,
                Message = "Report dismissed.",
            });
        })
        .WithName("DismissReport")
        .WithSummary("Dismisses a problem report without action.")
        .Produces<SubmitReportResponse>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        return app;
    }

    private static string? ExtractJsonField(string? json, string field)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(field, out var val) ? val.GetString() : null;
        }
        catch { return null; }
    }
}

// ── Request/Response DTOs ──

public sealed class SubmitReportRequest
{
    [JsonPropertyName("entity_id")]
    public Guid EntityId { get; set; }

    [JsonPropertyName("item_title")]
    public string? ItemTitle { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("reporter_name")]
    public string? ReporterName { get; set; }
}

public sealed class SubmitReportResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

public sealed class ReportEntryResponse
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("occurred_at")]
    public string OccurredAt { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("note")]
    public string Note { get; set; } = "";

    [JsonPropertyName("reporter_name")]
    public string ReporterName { get; set; } = "";

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }
}
