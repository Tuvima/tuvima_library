using System.Net;
using MediaEngine.Api.Services;
using MediaEngine.Domain.Contracts;

namespace MediaEngine.Api.Middleware;

/// <summary>
/// Authenticates every incoming HTTP request using one of three methods:
///
/// 1. <c>X-Api-Key</c> header — the key is hashed and looked up in the database.
///    On match, <c>HttpContext.Items["ApiKeyRole"]</c> is set to the key's role.
///
/// 2. Localhost bypass — if enabled (<c>MediaEngine:Security:LocalhostBypass = true</c>,
///    which is the default), requests from loopback addresses are treated as
///    Administrator. This preserves the existing local development experience.
///
/// 3. Exempt paths — <c>/system/status</c>, <c>/swagger*</c>, and the SignalR
///    negotiate endpoint are exempt. They pass through without authentication.
///
/// All other requests receive 401 Unauthorized.
///
/// SECURITY: The raw key value from the header is NEVER logged.
/// </summary>
public sealed class ApiKeyMiddleware(RequestDelegate next)
{
    private const string HeaderName = "X-Api-Key";

    /// <summary>
    /// Paths that never require authentication.
    /// </summary>
    private static readonly string[] ExemptPaths =
    [
        "/system/status",
    ];

    /// <summary>
    /// Path prefixes that never require authentication (Swagger, SignalR negotiate).
    /// </summary>
    private static readonly string[] ExemptPrefixes =
    [
        "/swagger",
        "/hubs/intercom",
    ];

    public async Task InvokeAsync(HttpContext ctx, IApiKeyRepository repo, IConfiguration config)
    {
        var path = ctx.Request.Path.Value ?? string.Empty;

        // ── 1. Exempt paths pass through unconditionally ──────────────────────
        if (IsExempt(path))
        {
            await next(ctx).ConfigureAwait(false);
            return;
        }

        // ── 2. If X-Api-Key header is present, validate it ───────────────────
        if (ctx.Request.Headers.TryGetValue(HeaderName, out var rawValues))
        {
            var rawKey = rawValues.ToString();
            if (string.IsNullOrWhiteSpace(rawKey))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsJsonAsync(new { error = "X-Api-Key header is empty." })
                                   .ConfigureAwait(false);
                return;
            }

            // Hash the incoming key and look it up — the plaintext is NEVER logged.
            var hashedKey = ApiKeyService.HashKey(rawKey);
            var match     = await repo.FindByHashedKeyAsync(hashedKey, ctx.RequestAborted)
                                       .ConfigureAwait(false);

            if (match is null)
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsJsonAsync(new { error = "Invalid API key." })
                                   .ConfigureAwait(false);
                return;
            }

            // Key is valid — annotate the context for downstream role filters.
            ctx.Items["ApiKeyId"]    = match.Id.ToString();
            ctx.Items["ApiKeyLabel"] = match.Label;
            ctx.Items["ApiKeyRole"]  = match.Role;

            await next(ctx).ConfigureAwait(false);
            return;
        }

        // ── 3. No header — check localhost bypass ────────────────────────────
        var bypassEnabled = config.GetValue("MediaEngine:Security:LocalhostBypass", true);
        if (bypassEnabled && IsLoopback(ctx.Connection.RemoteIpAddress))
        {
            // Localhost callers get full Administrator access when bypass is enabled.
            ctx.Items["ApiKeyRole"] = "Administrator";
            await next(ctx).ConfigureAwait(false);
            return;
        }

        // ── 4. No key, not exempt, not localhost — reject ────────────────────
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await ctx.Response.WriteAsJsonAsync(new { error = "Authentication required. Provide X-Api-Key header." })
                           .ConfigureAwait(false);
    }

    private static bool IsExempt(string path)
    {
        foreach (var exempt in ExemptPaths)
        {
            if (path.Equals(exempt, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var prefix in ExemptPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsLoopback(IPAddress? ip) =>
        ip is not null && IPAddress.IsLoopback(ip);
}
