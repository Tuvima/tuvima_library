using MediaEngine.Api.Services;

namespace MediaEngine.Api.Middleware;

public sealed class InteractiveRequestTrackingMiddleware(
    RequestDelegate next,
    InteractiveRequestTracker tracker)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!ShouldTrack(context.Request.Path))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        using var request = tracker.Begin();
        await next(context).ConfigureAwait(false);
    }

    private static bool ShouldTrack(PathString path) =>
        !path.StartsWithSegments("/health")
        && !path.StartsWithSegments("/swagger")
        && !path.StartsWithSegments("/intercom");
}
