using MediaEngine.Api.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace MediaEngine.Api.Security;

/// <summary>
/// SignalR collection filter that authenticates connections to <c>/intercom</c>.
///
/// Connections require a short-lived, purpose-specific token in a request header.
/// Long-lived API keys and query-string credentials are deliberately rejected.
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
        {
            throw new HubException("Connection rejected: missing HTTP context.");
        }

        var connectionLimiter = httpCtx.RequestServices.GetRequiredService<IntercomConnectionLimiter>();
        var payload = httpCtx.Items.TryGetValue(typeof(IntercomTokenPayload), out var validated)
            ? validated as IntercomTokenPayload
            : null;
        if (payload is null)
        {
            throw new HubException("Connection rejected: a valid Intercom token is required.");
        }

        if (!connectionLimiter.TryAcquire(payload.SessionId))
        {
            throw new HubException("Connection rejected: session connection limit reached.");
        }

        context.Context.Items["IntercomSessionId"] = payload.SessionId;
        var audiences = httpCtx.RequestServices.GetRequiredService<IntercomAudienceRegistry>();
        audiences.Add(new(context.Context.ConnectionId, payload.SessionId, payload.AccountId));
        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch
        {
            audiences.Remove(context.Context.ConnectionId);
            connectionLimiter.Release(payload.SessionId);
            throw;
        }
    }

    public async Task OnDisconnectedAsync(
        HubLifetimeContext context,
        Exception? exception,
        Func<HubLifetimeContext, Exception?, Task> next)
    {
        context.Context.GetHttpContext()?.RequestServices
            .GetRequiredService<IntercomAudienceRegistry>()
            .Remove(context.Context.ConnectionId);
        if (context.Context.Items.TryGetValue("IntercomSessionId", out var value) && value is Guid sessionId)
        {
            context.Context.GetHttpContext()?.RequestServices
                .GetRequiredService<IntercomConnectionLimiter>()
                .Release(sessionId);
        }

        await next(context, exception).ConfigureAwait(false);
    }
}
