using System.Reflection;
using MediaEngine.Api.Models;
using MediaEngine.Api.Security;
using MediaEngine.Ingestion.Contracts;

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
        app.MapGet("/system/status", () =>
            Results.Ok(new SystemStatusResponse
            {
                Status  = "ok",
                Version = AppVersion,
            }))
        .WithTags("System")
        .WithName("GetSystemStatus")
        .WithSummary("Returns service health and version. Used by external apps to test connectivity.")
        .Produces<SystemStatusResponse>(StatusCodes.Status200OK);

        app.MapGet("/system/watcher-status", (IFileWatcher watcher) =>
            Results.Ok(new
            {
                running         = watcher.IsRunning,
                directory_count = watcher.WatchedPaths.Count,
                directories     = watcher.WatchedPaths,
                event_count     = watcher.EventCount,
                last_event_at   = watcher.LastEventAt,
            }))
        .WithTags("System")
        .WithName("GetWatcherStatus")
        .WithSummary("Returns file watcher diagnostic status.")
        .RequireAdmin();

        return app;
    }
}
