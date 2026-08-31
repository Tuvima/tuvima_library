using System.Security.Cryptography;
using System.Text.Json;
using MediaEngine.Domain.Contracts;
using Microsoft.AspNetCore.DataProtection;

namespace MediaEngine.Api.Services.Playback;

public sealed class HlsAccessGrantService
{
    private readonly IDataProtector _protector;
    private readonly IConfigurationLoader _configuration;
    private readonly TimeProvider _timeProvider;

    public HlsAccessGrantService(
        IDataProtectionProvider provider,
        IConfigurationLoader configuration,
        TimeProvider? timeProvider = null)
    {
        _protector = provider.CreateProtector("Tuvima.Library.Playback.HlsAccess.v1");
        _configuration = configuration;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public HlsAccessGrant Create(Guid assetId, Guid packageId)
    {
        var minutes = Math.Clamp(
            _configuration.LoadTranscoding().AdaptiveHls.AccessLifetimeMinutes,
            15,
            720);
        var expiresAt = _timeProvider.GetUtcNow().AddMinutes(minutes);
        var payload = JsonSerializer.Serialize(new HlsGrantPayload(assetId, packageId, expiresAt.ToUnixTimeSeconds()));
        return new HlsAccessGrant(_protector.Protect(payload), expiresAt);
    }

    public bool TryValidate(string? token, Guid packageId, out Guid assetId)
    {
        assetId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2048) return false;
        try
        {
            var json = _protector.Unprotect(token);
            var payload = JsonSerializer.Deserialize<HlsGrantPayload>(json);
            if (payload is null
                || payload.PackageId != packageId
                || payload.AssetId == Guid.Empty
                || DateTimeOffset.FromUnixTimeSeconds(payload.ExpiresAtUnixSeconds) <= _timeProvider.GetUtcNow())
                return false;
            assetId = payload.AssetId;
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private sealed record HlsGrantPayload(Guid AssetId, Guid PackageId, long ExpiresAtUnixSeconds);
}

public sealed record HlsAccessGrant(string Value, DateTimeOffset ExpiresAt);
