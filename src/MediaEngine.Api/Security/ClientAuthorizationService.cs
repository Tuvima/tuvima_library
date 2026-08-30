using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediaEngine.Contracts.Authentication;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;

namespace MediaEngine.Api.Security;

public sealed record ClientAccessIdentity(ClientToken Token, ClientDevice Device, string Role);

public sealed record ClientTokenResult(OAuthTokenResponse? Success, OAuthErrorResponse? Error)
{
    public static ClientTokenResult Failed(string error, string description, int? interval = null) =>
        new(null, new OAuthErrorResponse { Error = error, ErrorDescription = description, Interval = interval });
}

public sealed class ClientAuthorizationService(
    IClientAuthorizationRepository repository,
    IProfileRepository profiles,
    TimeProvider timeProvider)
{
    public const string DeviceGrantType = "urn:ietf:params:oauth:grant-type:device_code";
    public const string RefreshGrantType = "refresh_token";
    public static readonly TimeSpan DeviceCodeLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);
    private const int InitialPollIntervalSeconds = 5;
    private const string UserCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private static readonly HashSet<string> ValidDeviceClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "web", "mobile", "television", "automotive",
    };

    public async Task<DeviceAuthorizationResponse> BeginAsync(
        DeviceAuthorizationRequest request,
        string verificationBaseUri,
        CancellationToken ct = default)
    {
        var clientId = RequiredText(request.ClientId, nameof(request.ClientId), 100);
        var now = timeProvider.GetUtcNow();
        var deviceCode = RandomToken(32);
        var userCode = RandomUserCode();
        var scopes = NormalizeScopes(
            request.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            useDefaultsWhenEmpty: true);
        var deviceClass = NormalizeDeviceClass(request.DeviceClass);
        var capabilities = NormalizeCapabilities(request.Capabilities);

        await repository.InsertPairingAsync(new DevicePairingRequest
        {
            Id = Guid.NewGuid(),
            DeviceCodeHash = Hash(deviceCode),
            UserCodeHash = Hash(NormalizeUserCode(userCode)),
            ClientId = clientId,
            ClientName = Text(request.ClientName, 100, clientId),
            ClientVersion = Text(request.ClientVersion, 50, "unknown"),
            DeviceName = Text(request.DeviceName, 100, "Unknown device"),
            DeviceClass = deviceClass,
            RequestedScopes = JoinScopes(scopes),
            CapabilitiesJson = JsonSerializer.Serialize(capabilities),
            Status = "pending",
            PollIntervalSeconds = InitialPollIntervalSeconds,
            CreatedAt = now,
            ExpiresAt = now.Add(DeviceCodeLifetime),
        }, ct).ConfigureAwait(false);

        var verificationUri = verificationBaseUri.TrimEnd('/') + "/pair";
        return new DeviceAuthorizationResponse
        {
            DeviceCode = deviceCode,
            UserCode = userCode,
            VerificationUri = verificationUri,
            VerificationUriComplete = $"{verificationUri}?user_code={Uri.EscapeDataString(userCode)}",
            ExpiresIn = checked((int)DeviceCodeLifetime.TotalSeconds),
            Interval = InitialPollIntervalSeconds,
        };
    }

    public async Task<PairingReviewResponse?> ReviewAsync(string userCode, CancellationToken ct = default)
    {
        var pairing = await repository.GetPairingByUserCodeHashAsync(Hash(NormalizeUserCode(userCode)), ct).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();
        if (pairing is null || pairing.Status != "pending" || pairing.ExpiresAt <= now)
        {
            return null;
        }

        return new PairingReviewResponse
        {
            RequestId = pairing.Id,
            ClientId = pairing.ClientId,
            ClientName = pairing.ClientName,
            ClientVersion = pairing.ClientVersion,
            DeviceName = pairing.DeviceName,
            DeviceClass = pairing.DeviceClass,
            RequestedScopes = SplitScopes(pairing.RequestedScopes),
            ExpiresAt = pairing.ExpiresAt,
        };
    }

    public async Task<bool> DecideAsync(
        PairingDecisionRequest request,
        Guid profileId,
        Guid approvedByProfileId,
        CancellationToken ct = default)
    {
        var pairing = await repository.GetPairingByUserCodeHashAsync(Hash(NormalizeUserCode(request.UserCode)), ct).ConfigureAwait(false);
        if (pairing is null)
        {
            return false;
        }

        var requested = SplitScopes(pairing.RequestedScopes);
        var approved = request.Scopes.Count == 0
            ? requested
            : NormalizeScopes(request.Scopes).Where(requested.Contains).ToArray();
        return await repository.DecidePairingAsync(
            pairing.Id,
            request.Approved,
            profileId,
            approvedByProfileId,
            JoinScopes(approved),
            timeProvider.GetUtcNow(),
            ct).ConfigureAwait(false);
    }

    public Task<ClientTokenResult> ExchangeAsync(OAuthTokenRequest request, CancellationToken ct = default) =>
        request.GrantType switch
        {
            DeviceGrantType => ExchangeDeviceCodeAsync(request, ct),
            RefreshGrantType => RefreshAsync(request, ct),
            _ => Task.FromResult(ClientTokenResult.Failed("unsupported_grant_type", "The requested grant type is not supported.")),
        };

    public async Task<ClientAccessIdentity?> ValidateAccessTokenAsync(string plaintextToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(plaintextToken) || plaintextToken.Length > 512)
        {
            return null;
        }

        var match = await repository.FindActiveAccessTokenAsync(Hash(plaintextToken), timeProvider.GetUtcNow(), ct).ConfigureAwait(false);
        if (match is null)
        {
            return null;
        }

        var profile = await profiles.GetByIdAsync(match.Value.Device.ProfileId, ct).ConfigureAwait(false);
        return profile is null
            ? null
            : new ClientAccessIdentity(match.Value.Token, match.Value.Device, profile.Role.ToString());
    }

    public Task<IReadOnlyList<ClientDevice>> GetDevicesAsync(Guid profileId, CancellationToken ct = default) =>
        repository.GetDevicesAsync(profileId, ct);

    public Task<ClientDevice?> GetDeviceAsync(Guid deviceId, CancellationToken ct = default) =>
        repository.GetDeviceAsync(deviceId, ct);

    public Task<bool> UpdateCapabilitiesAsync(Guid deviceId, ClientCapabilitiesDto capabilities, CancellationToken ct = default) =>
        repository.UpdateCapabilitiesAsync(
            deviceId,
            JsonSerializer.Serialize(NormalizeCapabilities(capabilities)),
            timeProvider.GetUtcNow(),
            ct);

    public Task<bool> RevokeDeviceAsync(Guid deviceId, Guid profileId, CancellationToken ct = default) =>
        repository.RevokeDeviceAsync(deviceId, profileId, timeProvider.GetUtcNow(), "user_revoked", ct);

    private async Task<ClientTokenResult> ExchangeDeviceCodeAsync(OAuthTokenRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceCode))
        {
            return ClientTokenResult.Failed("invalid_request", "device_code is required.");
        }

        var pairing = await repository.GetPairingByDeviceCodeHashAsync(Hash(request.DeviceCode), ct).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();
        if (pairing is null || !string.Equals(pairing.ClientId, request.ClientId, StringComparison.Ordinal))
        {
            return ClientTokenResult.Failed("invalid_grant", "The device code is invalid.");
        }
        if (pairing.ExpiresAt <= now)
        {
            return ClientTokenResult.Failed("expired_token", "The device code has expired.");
        }

        if (pairing.LastPolledAt is { } lastPoll
            && now < lastPoll.AddSeconds(pairing.PollIntervalSeconds))
        {
            var slower = Math.Min(60, pairing.PollIntervalSeconds + 5);
            await repository.RecordPairingPollAsync(pairing.Id, now, slower, ct).ConfigureAwait(false);
            return ClientTokenResult.Failed("slow_down", "Polling is faster than the permitted interval.", slower);
        }

        await repository.RecordPairingPollAsync(pairing.Id, now, pairing.PollIntervalSeconds, ct).ConfigureAwait(false);
        if (pairing.Status == "pending")
        {
            return ClientTokenResult.Failed("authorization_pending", "The device is waiting for approval.", pairing.PollIntervalSeconds);
        }
        if (pairing.Status == "denied")
        {
            return ClientTokenResult.Failed("access_denied", "The device request was denied.");
        }
        if (pairing.Status != "approved" || pairing.ProfileId is not Guid profileId)
        {
            return ClientTokenResult.Failed("expired_token", "The device authorization has already been consumed.");
        }

        var deviceId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var scopes = pairing.ApprovedScopes;
        var access = NewToken(deviceId, profileId, familyId, "access", scopes, 0, AccessTokenLifetime, now);
        var refresh = NewToken(deviceId, profileId, familyId, "refresh", scopes, 0, RefreshTokenLifetime, now);
        var device = new ClientDevice
        {
            Id = deviceId,
            ProfileId = profileId,
            DeviceName = pairing.DeviceName,
            DeviceClass = pairing.DeviceClass,
            ClientId = pairing.ClientId,
            ClientName = pairing.ClientName,
            ClientVersion = pairing.ClientVersion,
            Scopes = scopes,
            CapabilitiesJson = pairing.CapabilitiesJson,
            CreatedAt = now,
            LastSeenAt = now,
        };

        if (!await repository.ConsumePairingAsync(pairing, device, access.Entity, refresh.Entity, now, ct).ConfigureAwait(false))
        {
            return ClientTokenResult.Failed("expired_token", "The device authorization has already been consumed.");
        }

        return Success(access.Plaintext, refresh.Plaintext, deviceId, profileId, scopes);
    }

    private async Task<ClientTokenResult> RefreshAsync(OAuthTokenRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return ClientTokenResult.Failed("invalid_request", "refresh_token is required.");
        }

        var match = await repository.FindRefreshTokenAsync(Hash(request.RefreshToken), ct).ConfigureAwait(false);
        if (match is null || !string.Equals(match.Value.Device.ClientId, request.ClientId, StringComparison.Ordinal))
        {
            return ClientTokenResult.Failed("invalid_grant", "The refresh token is invalid.");
        }

        var current = match.Value.Token;
        var now = timeProvider.GetUtcNow();
        if (current.ConsumedAt is not null || current.RevokedAt is not null)
        {
            await repository.RevokeTokenFamilyAsync(current.TokenFamilyId, now, "refresh_token_replay", ct).ConfigureAwait(false);
            return ClientTokenResult.Failed("invalid_grant", "Refresh token replay was detected and the device grant was revoked.");
        }
        if (current.ExpiresAt <= now || !match.Value.Device.IsActive)
        {
            return ClientTokenResult.Failed("invalid_grant", "The refresh token is expired or revoked.");
        }

        var nextGeneration = checked(current.Generation + 1);
        var access = NewToken(current.DeviceId, current.ProfileId, current.TokenFamilyId, "access", current.Scopes, nextGeneration, AccessTokenLifetime, now);
        var refresh = NewToken(current.DeviceId, current.ProfileId, current.TokenFamilyId, "refresh", current.Scopes, nextGeneration, RefreshTokenLifetime, now);
        if (!await repository.RotateRefreshTokenAsync(current, access.Entity, refresh.Entity, now, ct).ConfigureAwait(false))
        {
            await repository.RevokeTokenFamilyAsync(current.TokenFamilyId, now, "refresh_token_replay", ct).ConfigureAwait(false);
            return ClientTokenResult.Failed("invalid_grant", "Refresh token replay was detected and the device grant was revoked.");
        }

        return Success(access.Plaintext, refresh.Plaintext, current.DeviceId, current.ProfileId, current.Scopes);
    }

    private static ClientTokenResult Success(string access, string refresh, Guid deviceId, Guid profileId, string scopes) =>
        new(new OAuthTokenResponse
        {
            AccessToken = access,
            RefreshToken = refresh,
            ExpiresIn = checked((int)AccessTokenLifetime.TotalSeconds),
            Scope = scopes,
            DeviceId = deviceId,
            ProfileId = profileId,
        }, null);

    private static (ClientToken Entity, string Plaintext) NewToken(
        Guid deviceId,
        Guid profileId,
        Guid familyId,
        string kind,
        string scopes,
        int generation,
        TimeSpan lifetime,
        DateTimeOffset now)
    {
        var plaintext = RandomToken(32);
        return (new ClientToken
        {
            Id = Guid.NewGuid(),
            DeviceId = deviceId,
            ProfileId = profileId,
            TokenFamilyId = familyId,
            Kind = kind,
            TokenHash = Hash(plaintext),
            Scopes = scopes,
            Generation = generation,
            CreatedAt = now,
            ExpiresAt = now.Add(lifetime),
        }, plaintext);
    }

    public static IReadOnlyList<string> SplitScopes(string value) =>
        value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IReadOnlyList<string> NormalizeScopes(IEnumerable<string> scopes, bool useDefaultsWhenEmpty = false)
    {
        var supplied = scopes
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => scope.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var unknown = supplied.Where(scope => !ClientApiScopes.Consumer.Contains(scope, StringComparer.Ordinal)).ToArray();
        if (unknown.Length > 0)
            throw new ArgumentException($"Unsupported scope: {string.Join(", ", unknown)}.");

        var requested = supplied
            .Order(StringComparer.Ordinal)
            .ToArray();
        return requested.Length == 0 && useDefaultsWhenEmpty ? ClientApiScopes.Default : requested;
    }

    private static string JoinScopes(IEnumerable<string> scopes) => string.Join(' ', scopes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

    private static ClientCapabilitiesDto NormalizeCapabilities(ClientCapabilitiesDto source) => new()
    {
        SchemaVersion = 1,
        Containers = NormalizeValues(source.Containers, 20),
        VideoCodecs = NormalizeValues(source.VideoCodecs, 20),
        AudioCodecs = NormalizeValues(source.AudioCodecs, 20),
        SubtitleFormats = NormalizeValues(source.SubtitleFormats, 20),
        Protocols = NormalizeValues(source.Protocols, 10),
        MaxWidth = Clamp(source.MaxWidth, 320, 16384),
        MaxHeight = Clamp(source.MaxHeight, 240, 8640),
        MaxBitrateKbps = Clamp(source.MaxBitrateKbps, 64, 500_000),
        MaxAudioChannels = Clamp(source.MaxAudioChannels, 1, 32),
        SupportsHdr = source.SupportsHdr,
        SupportsPlaybackSpeed = source.SupportsPlaybackSpeed,
        SupportsOfflineDownloads = source.SupportsOfflineDownloads,
    };

    private static IReadOnlyList<string> NormalizeValues(IEnumerable<string> values, int maximum) =>
        values.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToLowerInvariant())
            .Where(value => value.Length <= 40)
            .Distinct(StringComparer.Ordinal)
            .Take(maximum)
            .ToArray();

    private static int? Clamp(int? value, int minimum, int maximum) => value.HasValue ? Math.Clamp(value.Value, minimum, maximum) : null;

    private static string NormalizeDeviceClass(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is not null && ValidDeviceClasses.Contains(normalized) ? normalized : "television";
    }

    private static string NormalizeUserCode(string value) =>
        new(value.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static string RequiredText(string value, string name, int maximum)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return Text(value, maximum, string.Empty);
    }

    private static string Text(string? value, int maximum, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return normalized.Length <= maximum ? normalized : normalized[..maximum];
    }

    private static string RandomUserCode()
    {
        Span<char> characters = stackalloc char[8];
        for (var i = 0; i < characters.Length; i++)
        {
            characters[i] = UserCodeAlphabet[RandomNumberGenerator.GetInt32(UserCodeAlphabet.Length)];
        }
        return $"{new string(characters[..4])}-{new string(characters[4..])}";
    }

    private static string RandomToken(int bytes) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Hash(string plaintext) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext)));
}
