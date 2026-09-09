using MediaEngine.Api.Http;
using MediaEngine.Api.Security;
using MediaEngine.Api.Services.Playback;
using MediaEngine.Contracts.Paging;
using MediaEngine.Contracts.Playback;
using MediaEngine.Domain.Authorization;

namespace MediaEngine.Api.Endpoints;

public static class PlaybackTelemetryEndpoints
{
    public static IEndpointRouteBuilder MapPlaybackTelemetryEndpoints(this IEndpointRouteBuilder app)
    {
        var playback = app.MapGroup("/api/v1/playback")
            .WithTags("Playback telemetry");

        playback.MapGet("/sessions", async (int? limit, string? cursor,
            [Microsoft.AspNetCore.Mvc.FromServices] PlaybackTelemetryReadService service, CancellationToken ct) =>
            await Page(() => service.GetActiveAsync(
                ApplicationPermissionIds.PlaybackSessionsRead, PagedRequest.From(null, limit, defaultLimit: 50, maxLimit: 200).Limit, cursor, ct)))
            .WithName("GetActivePlaybackTelemetrySessions")
            .WithSummary("Return current playback sessions within the caller's authorized resource scope.")
            .Produces<PlaybackTelemetryPageDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAdministratorOrApplication(ApplicationPermissionIds.PlaybackSessionsRead);

        playback.MapGet("/history", async (DateTimeOffset? from, DateTimeOffset? to,
            int? limit, string? cursor,
            [Microsoft.AspNetCore.Mvc.FromServices] PlaybackTelemetryReadService service, CancellationToken ct) =>
            await Page(() => service.GetHistoryAsync(
                ApplicationPermissionIds.PlaybackHistoryRead, from, to, PagedRequest.From(null, limit, defaultLimit: 50, maxLimit: 200).Limit, cursor, ct), from, to))
            .WithName("GetPlaybackTelemetryHistory")
            .WithSummary("Return durable playback history within the caller's authorized resource scope.")
            .Produces<PlaybackTelemetryPageDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAdministratorOrApplication(ApplicationPermissionIds.PlaybackHistoryRead);

        var analytics = app.MapGroup("/api/v1/analytics")
            .WithTags("Playback analytics");

        analytics.MapGet("/playback", async (DateTimeOffset? from, DateTimeOffset? to,
            [Microsoft.AspNetCore.Mvc.FromServices] PlaybackTelemetryReadService service, CancellationToken ct) =>
            await Aggregate(() => service.GetPlaybackAsync(
                ApplicationPermissionIds.AnalyticsPlaybackRead, from, to, ct), from, to))
            .WithName("GetPlaybackAnalytics")
            .Produces<PlaybackAnalyticsDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAdministratorOrApplication(ApplicationPermissionIds.AnalyticsPlaybackRead);

        MapGroups(analytics, "/users", "users", "GetPlaybackUserAnalytics", ApplicationPermissionIds.AnalyticsUsersRead);
        MapGroups(analytics, "/libraries", "libraries", "GetPlaybackLibraryAnalytics", ApplicationPermissionIds.AnalyticsLibraryRead);
        MapGroups(analytics, "/devices", "devices", "GetPlaybackDeviceAnalytics", ApplicationPermissionIds.AnalyticsDevicesRead);
        return app;
    }

    private static void MapGroups(
        RouteGroupBuilder group,
        string route,
        string dimension,
        string name,
        ApplicationPermissionId permission)
    {
        group.MapGet(route, async (DateTimeOffset? from, DateTimeOffset? to, int? limit,
            [Microsoft.AspNetCore.Mvc.FromServices] PlaybackTelemetryReadService service, CancellationToken ct) =>
            await Groups(() => service.GetGroupsAsync(permission, dimension, from, to, PagedRequest.From(null, limit, defaultLimit: 50, maxLimit: 200).Limit, ct), from, to))
            .WithName(name)
            .Produces<PlaybackAnalyticsGroupsDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAdministratorOrApplication(permission);
    }

    private static async Task<IResult> Page(
        Func<Task<PlaybackTelemetryPageDto?>> read,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null)
    {
        if (!ValidWindow(from, to))
        {
            return ApiErrors.BadRequest("The playback telemetry time window is invalid.");
        }

        try
        {
            return await read().ConfigureAwait(false) is { } value
                ? Results.Ok(value)
                : ApiErrors.Forbidden("Playback telemetry access is denied.");
        }
        catch (ArgumentException exception)
        {
            return ApiErrors.BadRequest(exception.Message);
        }
    }

    private static async Task<IResult> Aggregate(
        Func<Task<PlaybackAnalyticsDto?>> read,
        DateTimeOffset? from,
        DateTimeOffset? to)
    {
        if (!ValidWindow(from, to))
        {
            return ApiErrors.BadRequest("The playback analytics time window is invalid.");
        }

        return await read().ConfigureAwait(false) is { } value
            ? Results.Ok(value)
            : ApiErrors.Forbidden("Playback analytics access is denied.");
    }

    private static async Task<IResult> Groups(
        Func<Task<PlaybackAnalyticsGroupsDto?>> read,
        DateTimeOffset? from,
        DateTimeOffset? to)
    {
        if (!ValidWindow(from, to))
        {
            return ApiErrors.BadRequest("The playback analytics time window is invalid.");
        }

        return await read().ConfigureAwait(false) is { } value
            ? Results.Ok(value)
            : ApiErrors.Forbidden("Playback analytics access is denied.");
    }

    private static bool ValidWindow(DateTimeOffset? from, DateTimeOffset? to) =>
        !from.HasValue || !to.HasValue || from.Value < to.Value;

}
