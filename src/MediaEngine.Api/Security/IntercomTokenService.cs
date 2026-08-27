using System.Collections.Concurrent;
using System.Text.Json;
using MediaEngine.Domain.Contracts;
using Microsoft.AspNetCore.DataProtection;

namespace MediaEngine.Api.Security;

public sealed record IntercomTokenPayload(Guid SessionId, Guid ProfileId, string Audience, string TokenId);

public sealed class IntercomTokenService(IDataProtectionProvider provider, IIdentityRepository identities)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly ITimeLimitedDataProtector _protector = provider
        .CreateProtector("Tuvima.Intercom.Connection.v1")
        .ToTimeLimitedDataProtector();

    public (string Token, DateTimeOffset ExpiresAt) Create(Guid sessionId, Guid profileId)
    {
        var expires = DateTimeOffset.UtcNow.Add(Lifetime);
        var payload = new IntercomTokenPayload(sessionId, profileId, "intercom", Guid.NewGuid().ToString("N"));
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
            return session is not null && session.ProfileId == payload.ProfileId && session.IsActive(DateTimeOffset.UtcNow)
                ? payload
                : null;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
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
