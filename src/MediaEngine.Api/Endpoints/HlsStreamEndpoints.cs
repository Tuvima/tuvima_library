using MediaEngine.Api.Services.Playback;

namespace MediaEngine.Api.Endpoints;

public static class HlsStreamEndpoints
{
    public static IEndpointRouteBuilder MapHlsStreamEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMethods(
            "/stream/hls/{grant}/{packageId:guid}/{**resourcePath}",
            [HttpMethods.Get, HttpMethods.Head],
            HandleAsync)
            .WithName("GetAdaptiveHlsResource")
            .WithSummary("Serve one resource from a path-scoped adaptive HLS package grant.")
            .AllowAnonymous()
            .RequireRateLimiting("streaming");
        return app;
    }

    private static async Task HandleAsync(
        string grant,
        Guid packageId,
        string? resourcePath,
        HttpContext context,
        HlsAccessGrantService grants,
        AdaptiveHlsService hls,
        CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        if (!grants.TryValidate(grant, packageId, out var assetId))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await using var resource = await hls.OpenResourceAsync(
            packageId,
            assetId,
            resourcePath ?? string.Empty,
            ct).ConfigureAwait(false);
        if (resource is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.ContentType = resource.ContentType;
        context.Response.ContentLength = resource.Stream.Length;
        if (!HttpMethods.IsHead(context.Request.Method))
            await resource.Stream.CopyToAsync(context.Response.Body, ct).ConfigureAwait(false);
    }
}
