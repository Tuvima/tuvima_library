using System.Net.Http.Headers;

namespace MediaEngine.Api.Security;

/// <summary>
/// Rejects unauthenticated SignalR negotiation and transport requests before the
/// Intercom hub runs. The hub filter remains the connection-lifecycle backstop.
/// </summary>
public sealed class IntercomTokenAuthenticationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IntercomTokenService tokens,
        ILogger<IntercomTokenAuthenticationMiddleware> logger)
    {
        if (!context.Request.Path.StartsWithSegments("/intercom"))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var rawToken = ReadBearerToken(context.Request.Headers.Authorization.ToString());
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            logger.LogWarning("Intercom request rejected because no bearer token was supplied.");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var payload = await tokens.ValidateAsync(rawToken, context.RequestAborted).ConfigureAwait(false);
        if (payload is null)
        {
            logger.LogWarning("Intercom request rejected because its bearer token is invalid or its session is no longer active.");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        context.Items[typeof(IntercomTokenPayload)] = payload;
        await next(context).ConfigureAwait(false);
    }

    private static string ReadBearerToken(string authorization)
    {
        return AuthenticationHeaderValue.TryParse(authorization, out var parsed)
               && parsed.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase)
            ? parsed.Parameter ?? string.Empty
            : string.Empty;
    }
}
