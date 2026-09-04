using System.Collections.Concurrent;
using System.Text.Json;
using MediaEngine.Domain.Contracts;
using Microsoft.AspNetCore.DataProtection;

namespace MediaEngine.Api.Security;

public sealed record IntercomTokenPayload(Guid SessionId, Guid AccountId, string Audience, string TokenId);

public sealed class IntercomTokenService(
    IDataProtectionProvider provider,
    IIdentityRepository identities,
    ILogger<IntercomTokenService> logger)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly ITimeLimitedDataProtector _protector = provider
        .CreateProtector("Tuvima.Intercom.Connection.v1")
        .ToTimeLimitedDataProtector();

    public (string Token, DateTimeOffset ExpiresAt) Create(Guid sessionId, Guid accountId)
    {
        var expires = DateTimeOffset.UtcNow.Add(Lifetime);
        var payload = new IntercomTokenPayload(sessionId, accountId, "intercom", Guid.NewGuid().ToString("N"));
        return (_protector.Protect(JsonSerializer.Serialize(payload), expires), expires);
    }

    public async Task<IntercomTokenPayload?> ValidateAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            var json = _protector.Unprotect(token, out _);
            var payload = JsonSerializer.Deserialize<IntercomTokenPayload>(json);
            if (payload is null || !payload.Audience.Equals("intercom", StringComparison.Ordinal)) return null;
            var session = await identities.GetSessionByIdAsync(payload.SessionId, ct).ConfigureAwait(false);
            if (session is null)
            {
                logger.LogWarning("Intercom token validation failed because session {SessionId} no longer exists.", payload.SessionId);
                return null;
            }

            if (session.AccountId != payload.AccountId)
            {
                logger.LogWarning(
                    "Intercom token validation failed because session {SessionId} belongs to a different owner profile.",
                    payload.SessionId);
                return null;
            }

            if (!session.IsActive(DateTimeOffset.UtcNow))
            {
                logger.LogWarning("Intercom token validation failed because session {SessionId} is expired or revoked.", payload.SessionId);
                return null;
            }

            return payload;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            logger.LogWarning("Intercom token validation failed because the protected token could not be read.");
            return null;
        }
    }
}

public sealed class IntercomConnectionLimiter
{
    private const int MaximumConnectionsPerSession = 5;
    private readonly ConcurrentDictionary<Guid, int> _counts = new();

    public bool TryAcquire(Guid sessionId)
    {
        while (true)
        {
            var current = _counts.GetOrAdd(sessionId, 0);
            if (current >= MaximumConnectionsPerSession) return false;
            if (_counts.TryUpdate(sessionId, current + 1, current)) return true;
        }
    }

    public void Release(Guid sessionId)
    {
        while (_counts.TryGetValue(sessionId, out var current))
        {
            if (current <= 1)
            {
                _counts.TryRemove(sessionId, out _);
                return;
            }
            if (_counts.TryUpdate(sessionId, current - 1, current)) return;
        }
    }
}
