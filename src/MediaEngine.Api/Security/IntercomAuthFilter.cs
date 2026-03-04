using System.Net;
using Microsoft.AspNetCore.SignalR;
using MediaEngine.Api.Services;
using MediaEngine.Domain.Contracts;

namespace MediaEngine.Api.Security;

/// <summary>
/// SignalR hub filter that authenticates connections to <c>/hubs/intercom</c>.
///
/// Authentication sources (checked in order):
/// 1. <c>X-Api-Key</c> header on the WebSocket upgrade request.
/// 2. <c>access_token</c> query-string parameter (for browser clients that
///    cannot set custom headers on WebSocket connections).
/// 3. Localhost bypass — if enabled in configuration and the remote IP is loopback.
///
/// When all three sources fail, the connection is rejected with a <see cref="HubException"/>.
/// </summary>
public sealed class IntercomAuthFilter : IHubFilter
{
    /// <inheritdoc/>
    public ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext context,
        Func<HubInvocationContext, ValueTask<object?>> next) =>
        next(context);

    /// <inheritdoc/>
    public async Task OnConnectedAsync(
        HubLifetimeContext context,
        Func<HubLifetimeContext, Task> next)
    {
        var httpCtx = context.Context.GetHttpContext();
        if (httpCtx is null)
            throw new HubException("Connection rejected: missing HTTP context.");

        var repo   = httpCtx.RequestServices.GetRequiredService<IApiKeyRepository>();
        var config = httpCtx.RequestServices.GetRequiredService<IConfiguration>();

        // 1. Check X-Api-Key header.
        string? rawKey = null;
        if (httpCtx.Request.Headers.TryGetValue("X-Api-Key", out var headerValues))
            rawKey = headerValues.ToString();

        // 2. Fallback: check access_token query string (browser WebSocket limitation).
        if (string.IsNullOrWhiteSpace(rawKey) &&
            httpCtx.Request.Query.TryGetValue("access_token", out var queryValues))
            rawKey = queryValues.ToString();

        if (!string.IsNullOrWhiteSpace(rawKey))
        {
            var hashedKey = ApiKeyService.HashKey(rawKey);
            var match = await repo.FindByHashedKeyAsync(hashedKey, httpCtx.RequestAborted)
                                  .ConfigureAwait(false);

            if (match is not null)
            {
                context.Context.Items["ApiKeyRole"] = match.Role;
                await next(context).ConfigureAwait(false);
                return;
            }

            throw new HubException("Connection rejected: invalid API key.");
        }

        // 3. Localhost bypass.
        var bypassEnabled = config.GetValue("MediaEngine:Security:LocalhostBypass", true);
        if (bypassEnabled && IsLoopback(httpCtx.Connection.RemoteIpAddress))
        {
            context.Context.Items["ApiKeyRole"] = "Administrator";
            await next(context).ConfigureAwait(false);
            return;
        }

        throw new HubException("Connection rejected: authentication required.");
    }

    private static bool IsLoopback(IPAddress? ip) =>
        ip is not null && IPAddress.IsLoopback(ip);
}
