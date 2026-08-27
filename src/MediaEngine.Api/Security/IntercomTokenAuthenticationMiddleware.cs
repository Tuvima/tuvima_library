namespace MediaEngine.Api.Security;

/// <summary>
/// Rejects unauthenticated SignalR negotiation and transport requests before the
/// Intercom hub runs. The hub filter remains the connection-lifecycle backstop.
/// </summary>
public sealed class IntercomTokenAuthenticationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IntercomTokenService tokens)
    {
        if (!context.Request.Path.StartsWithSegments("/intercom"))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var rawToken = context.Request.Headers["X-Tuvima-Intercom-Token"].ToString();
        var payload = await tokens.ValidateAsync(rawToken, context.RequestAborted).ConfigureAwait(false);
        if (payload is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        context.Items[typeof(IntercomTokenPayload)] = payload;
        await next(context).ConfigureAwait(false);
    }
}
