using System.Security.Cryptography;
using System.Text;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Identity.Contracts;
using Microsoft.AspNetCore.Identity;

namespace MediaEngine.Identity;

public sealed class FirstPartyIdentityService(
    IIdentityRepository identities,
    IProfileRepository profiles,
    IPasswordHasher<ProfileCredential> passwordHasher,
    TimeProvider timeProvider) : IFirstPartyIdentityService
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(14);
    private static readonly TimeSpan RecoveryLifetime = TimeSpan.FromDays(365);

    public Task<bool> IsAdministratorConfiguredAsync(CancellationToken ct = default) =>
        identities.HasAdministratorPasswordAsync(ct);

    public async Task<SessionIssueResult> BootstrapAdministratorAsync(
        string username,
        string password,
        string displayName,
        string deviceId,
        string deviceName,
        string client,
        CancellationToken ct = default)
    {
        if (await identities.HasAdministratorPasswordAsync(ct).ConfigureAwait(false))
            throw new InvalidOperationException("The administrator has already been configured.");

        ValidatePassword(password);
        var normalizedUsername = NormalizeUsername(username);
        var owner = await profiles.GetByIdAsync(Profile.SeedProfileId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The seeded Owner profile is unavailable.");

        owner.DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Owner" : displayName.Trim();
        owner.Role = ProfileRole.Administrator;
        if (!await profiles.UpdateAsync(owner, ct).ConfigureAwait(false))
            throw new InvalidOperationException("The Owner profile could not be updated.");

        var credential = NewCredential(owner.Id, ProfileCredentialKind.Password, normalizedUsername, password);
        await identities.UpsertCredentialAsync(credential, ct).ConfigureAwait(false);
        var codes = await ReplaceRecoveryCodesAsync(owner.Id, ct).ConfigureAwait(false);
        var issued = await IssueSessionAsync(owner, owner, credential.SecurityStamp, "Password", deviceId, deviceName, client, ct).ConfigureAwait(false);
        await AuditAsync(owner.Id, issued.Session.Id, "administrator_bootstrap", true, null, ct).ConfigureAwait(false);
        return issued with { RecoveryCodes = codes };
    }

    public async Task<AuthenticationAttemptResult> AuthenticatePasswordAsync(
        string username,
        string password,
        string deviceId,
        string deviceName,
        string client,
        CancellationToken ct = default)
    {
        var normalized = NormalizeUsername(username);
        var credential = await identities.GetCredentialByUsernameAsync(normalized, ct).ConfigureAwait(false);
        return await AuthenticateAsync(credential, password, deviceId, deviceName, client, ct).ConfigureAwait(false);
    }

    public async Task<AuthenticationAttemptResult> AuthenticatePinAsync(
        Guid profileId,
        string pin,
        string deviceId,
        string deviceName,
        string client,
        CancellationToken ct = default)
    {
        var credential = await identities.GetCredentialAsync(profileId, ProfileCredentialKind.ProfilePin, ct).ConfigureAwait(false);
        return await AuthenticateAsync(credential, pin, deviceId, deviceName, client, ct).ConfigureAwait(false);
    }

    public async Task<SessionIssueResult> CreateExternalSessionAsync(Guid profileId, string provider, string deviceId, string deviceName, string client, CancellationToken ct = default)
    {
        var profile = await profiles.GetByIdAsync(profileId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Profile '{profileId}' was not found.");
        var stamp = $"external:{provider.Trim().ToLowerInvariant()}";
        var issued = await IssueSessionAsync(profile, profile, stamp, "Oidc", deviceId, deviceName, client, ct).ConfigureAwait(false);
        await AuditAsync(profile.Id, issued.Session.Id, "login_oidc", true, provider, ct).ConfigureAwait(false);
        return issued;
    }

    public async Task<SessionValidationResult?> ValidateSessionAsync(string plaintextToken, bool touch = true, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(plaintextToken)) return null;
        var now = UtcNow;
        var session = await identities.GetSessionByTokenHashAsync(HashToken(plaintextToken), ct).ConfigureAwait(false);
        if (session is null || !session.IsActive(now)) return null;

        var profile = await profiles.GetByIdAsync(session.ProfileId, ct).ConfigureAwait(false);
        var active = await profiles.GetByIdAsync(session.ActiveProfileId, ct).ConfigureAwait(false);
        if (profile is null || active is null) return null;

        if (!session.AuthenticationMethod.Equals("Oidc", StringComparison.OrdinalIgnoreCase))
        {
            var kind = session.AuthenticationMethod.Equals("ProfilePin", StringComparison.OrdinalIgnoreCase)
                ? ProfileCredentialKind.ProfilePin
                : ProfileCredentialKind.Password;
            var credential = await identities.GetCredentialAsync(session.ProfileId, kind, ct).ConfigureAwait(false);
            if (credential is null || !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(credential.SecurityStamp),
                    Encoding.UTF8.GetBytes(session.SecurityStamp)))
                return null;
        }

        if (touch && now - session.LastSeenAt >= TimeSpan.FromMinutes(1))
        {
            await identities.TouchSessionAsync(session.Id, now, ct).ConfigureAwait(false);
            session.LastSeenAt = now;
        }

        return new SessionValidationResult(session, profile, active);
    }

    public Task<IReadOnlyList<AuthSession>> GetSessionsAsync(Guid profileId, CancellationToken ct = default) =>
        identities.GetSessionsAsync(profileId, ct);

    public async Task<bool> RevokeSessionAsync(Guid sessionId, string reason, CancellationToken ct = default)
    {
        var session = await identities.GetSessionByIdAsync(sessionId, ct).ConfigureAwait(false);
        var revoked = await identities.RevokeSessionAsync(sessionId, UtcNow, SanitizeReason(reason), ct).ConfigureAwait(false);
        if (revoked) await AuditAsync(session?.ProfileId, sessionId, "session_revoked", true, reason, ct).ConfigureAwait(false);
        return revoked;
    }

    public Task<int> RevokeOtherSessionsAsync(Guid profileId, Guid currentSessionId, string reason, CancellationToken ct = default) =>
        identities.RevokeProfileSessionsAsync(profileId, UtcNow, SanitizeReason(reason), currentSessionId, ct);

    public async Task ChangePasswordAsync(Guid profileId, string currentPassword, string newPassword, Guid? currentSessionId = null, CancellationToken ct = default)
    {
        ValidatePassword(newPassword);
        var credential = await identities.GetCredentialAsync(profileId, ProfileCredentialKind.Password, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("This profile does not have a local password.");
        if (!Verify(credential, currentPassword, out _))
            throw new UnauthorizedAccessException("The current password is incorrect.");

        credential.SecretHash = Hash(credential, newPassword);
        credential.SecurityStamp = NewSecurityStamp();
        credential.HashVersion = 1;
        credential.UpdatedAt = UtcNow;
        credential.FailedAttemptCount = 0;
        credential.LockedUntil = null;
        await identities.UpsertCredentialAsync(credential, ct).ConfigureAwait(false);
        await identities.RevokeProfileSessionsAsync(profileId, UtcNow, "password_changed", currentSessionId, ct).ConfigureAwait(false);
        await AuditAsync(profileId, currentSessionId, "password_changed", true, null, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> ResetPasswordWithRecoveryCodeAsync(string username, string recoveryCode, string newPassword, CancellationToken ct = default)
    {
        ValidatePassword(newPassword);
        var credential = await identities.GetCredentialByUsernameAsync(NormalizeUsername(username), ct).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The recovery information is invalid.");
        var code = await identities.GetActiveRecoveryCodeAsync(credential.ProfileId, HashToken(NormalizeRecoveryCode(recoveryCode)), UtcNow, ct).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The recovery information is invalid.");
        if (!await identities.ConsumeRecoveryCodeAsync(code.Id, UtcNow, ct).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The recovery information is invalid.");

        credential.SecretHash = Hash(credential, newPassword);
        credential.SecurityStamp = NewSecurityStamp();
        credential.UpdatedAt = UtcNow;
        credential.FailedAttemptCount = 0;
        credential.LockedUntil = null;
        await identities.UpsertCredentialAsync(credential, ct).ConfigureAwait(false);
        await identities.RevokeProfileSessionsAsync(credential.ProfileId, UtcNow, "password_recovered", null, ct).ConfigureAwait(false);
        var codes = await ReplaceRecoveryCodesAsync(credential.ProfileId, ct).ConfigureAwait(false);
        await AuditAsync(credential.ProfileId, null, "password_recovered", true, null, ct).ConfigureAwait(false);
        return codes;
    }

    public Task<IReadOnlyList<string>> RegenerateRecoveryCodesAsync(Guid profileId, CancellationToken ct = default) =>
        ReplaceRecoveryCodesAsync(profileId, ct);

    public async Task SetProfilePinAsync(Guid profileId, string? pin, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pin))
        {
            if (await profiles.GetByIdAsync(profileId, ct).ConfigureAwait(false) is null)
                throw new KeyNotFoundException($"Profile '{profileId}' was not found.");

            await identities.DeleteCredentialAsync(profileId, ProfileCredentialKind.ProfilePin, ct).ConfigureAwait(false);
            await identities.RevokeProfileSessionsAsync(profileId, UtcNow, "profile_pin_removed", null, ct).ConfigureAwait(false);
            await AuditAsync(profileId, null, "profile_pin_removed", true, null, ct).ConfigureAwait(false);
            return;
        }
        ValidatePin(pin);
        if (await profiles.GetByIdAsync(profileId, ct).ConfigureAwait(false) is null)
            throw new KeyNotFoundException($"Profile '{profileId}' was not found.");

        var existing = await identities.GetCredentialAsync(profileId, ProfileCredentialKind.ProfilePin, ct).ConfigureAwait(false);
        var credential = existing ?? NewCredential(profileId, ProfileCredentialKind.ProfilePin, null, pin);
        if (existing is not null)
        {
            credential.SecretHash = Hash(credential, pin);
            credential.SecurityStamp = NewSecurityStamp();
            credential.UpdatedAt = UtcNow;
            credential.FailedAttemptCount = 0;
            credential.LockedUntil = null;
        }
        await identities.UpsertCredentialAsync(credential, ct).ConfigureAwait(false);
        await identities.RevokeProfileSessionsAsync(profileId, UtcNow, "profile_pin_changed", null, ct).ConfigureAwait(false);
        await AuditAsync(profileId, null, "profile_pin_changed", true, null, ct).ConfigureAwait(false);
    }

    public async Task<SessionValidationResult> SwitchActiveProfileAsync(string sessionToken, Guid targetProfileId, string? secret, CancellationToken ct = default)
    {
        var current = await ValidateSessionAsync(sessionToken, false, ct).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The session is no longer valid.");
        var target = await profiles.GetByIdAsync(targetProfileId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Profile '{targetProfileId}' was not found.");

        if (target.Id != current.Profile.Id)
        {
            var password = await identities.GetCredentialAsync(target.Id, ProfileCredentialKind.Password, ct).ConfigureAwait(false);
            var pin = await identities.GetCredentialAsync(target.Id, ProfileCredentialKind.ProfilePin, ct).ConfigureAwait(false);
            var credential = password ?? pin;
            if (credential is null || string.IsNullOrEmpty(secret) || !Verify(credential, secret, out _))
                throw new UnauthorizedAccessException("That profile requires authentication.");
        }

        if (!await identities.UpdateActiveProfileAsync(current.Session.Id, target.Id, ct).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The session is no longer valid.");
        current.Session.ActiveProfileId = target.Id;
        await AuditAsync(current.Profile.Id, current.Session.Id, "active_profile_changed", true, target.Id.ToString("D"), ct).ConfigureAwait(false);
        return new SessionValidationResult(current.Session, current.Profile, target);
    }

    public async Task<bool> ValidateServiceCredentialAsync(string plaintextToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(plaintextToken)) return false;
        return await identities.GetServiceCredentialByHashAsync(HashToken(plaintextToken), ct).ConfigureAwait(false) is not null;
    }

    private async Task<AuthenticationAttemptResult> AuthenticateAsync(ProfileCredential? credential, string secret, string deviceId, string deviceName, string client, CancellationToken ct)
    {
        var now = UtcNow;
        if (credential is null)
        {
            await AuditAsync(null, null, "login_failed", false, "unknown_credential", ct).ConfigureAwait(false);
            return new(false, false, "Invalid credentials.", null);
        }
        if (credential.LockedUntil is { } lockedUntil && lockedUntil > now)
            return new(false, true, "Too many attempts. Try again later.", null);

        if (!Verify(credential, secret, out var rehashNeeded))
        {
            var failures = credential.FailedAttemptCount + 1;
            DateTimeOffset? lockout = failures >= MaxFailedAttempts ? now.Add(LockoutDuration) : null;
            await identities.UpdateCredentialAttemptAsync(credential.Id, failures, lockout, null, ct).ConfigureAwait(false);
            await AuditAsync(credential.ProfileId, null, "login_failed", false, lockout is null ? "invalid_secret" : "locked_out", ct).ConfigureAwait(false);
            return new(false, lockout is not null, "Invalid credentials.", null);
        }

        if (rehashNeeded)
        {
            credential.SecretHash = Hash(credential, secret);
            credential.HashVersion = 1;
            credential.UpdatedAt = now;
            await identities.UpsertCredentialAsync(credential, ct).ConfigureAwait(false);
        }
        await identities.UpdateCredentialAttemptAsync(credential.Id, 0, null, now, ct).ConfigureAwait(false);

        var profile = await profiles.GetByIdAsync(credential.ProfileId, ct).ConfigureAwait(false);
        if (profile is null) return new(false, false, "Invalid credentials.", null);
        var method = credential.Kind.ToString();
        var issued = await IssueSessionAsync(profile, profile, credential.SecurityStamp, method, deviceId, deviceName, client, ct).ConfigureAwait(false);
        await AuditAsync(profile.Id, issued.Session.Id, "login_local", true, method, ct).ConfigureAwait(false);
        return new(true, false, null, issued);
    }

    private async Task<SessionIssueResult> IssueSessionAsync(Profile profile, Profile activeProfile, string securityStamp, string method, string deviceId, string deviceName, string client, CancellationToken ct)
    {
        var now = UtcNow;
        var token = RandomToken(32);
        var session = new AuthSession
        {
            Id = Guid.NewGuid(), ProfileId = profile.Id, ActiveProfileId = activeProfile.Id,
            TokenHash = HashToken(token), DeviceId = Sanitize(deviceId, 100, "unknown"),
            DeviceName = Sanitize(deviceName, 100, "Unknown device"), Client = Sanitize(client, 200, "Dashboard"),
            AuthenticationMethod = method, SecurityStamp = securityStamp, CreatedAt = now,
            LastSeenAt = now, ExpiresAt = now.Add(SessionLifetime),
        };
        await identities.InsertSessionAsync(session, ct).ConfigureAwait(false);
        return new(session, profile, activeProfile, token, []);
    }

    private ProfileCredential NewCredential(Guid profileId, ProfileCredentialKind kind, string? normalizedUsername, string secret)
    {
        var now = UtcNow;
        var credential = new ProfileCredential
        {
            Id = Guid.NewGuid(), ProfileId = profileId, Kind = kind, NormalizedUsername = normalizedUsername,
            HashScheme = "aspnet-pbkdf2-v3", HashVersion = 1, SecurityStamp = NewSecurityStamp(),
            CreatedAt = now, UpdatedAt = now,
        };
        credential.SecretHash = Hash(credential, secret);
        return credential;
    }

    private async Task<IReadOnlyList<string>> ReplaceRecoveryCodesAsync(Guid profileId, CancellationToken ct)
    {
        var now = UtcNow;
        var plaintext = Enumerable.Range(0, 10).Select(_ => RecoveryCode()).ToArray();
        var rows = plaintext.Select(code => new PasswordRecoveryCode
        {
            Id = Guid.NewGuid(), ProfileId = profileId, CodeHash = HashToken(NormalizeRecoveryCode(code)),
            CreatedAt = now, ExpiresAt = now.Add(RecoveryLifetime),
        }).ToArray();
        await identities.DeleteRecoveryCodesAsync(profileId, ct).ConfigureAwait(false);
        await identities.InsertRecoveryCodesAsync(rows, ct).ConfigureAwait(false);
        return plaintext;
    }

    private string Hash(ProfileCredential credential, string secret) =>
        passwordHasher.HashPassword(credential, DomainSeparated(credential.Kind, secret));

    private bool Verify(ProfileCredential credential, string secret, out bool rehashNeeded)
    {
        var result = passwordHasher.VerifyHashedPassword(credential, credential.SecretHash, DomainSeparated(credential.Kind, secret));
        rehashNeeded = result == PasswordVerificationResult.SuccessRehashNeeded;
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }

    private Task AuditAsync(Guid? profileId, Guid? sessionId, string type, bool succeeded, string? detail, CancellationToken ct) =>
        identities.WriteAuditEventAsync(profileId, sessionId, type, succeeded, detail, UtcNow, ct);

    private DateTimeOffset UtcNow => timeProvider.GetUtcNow();
    private static string NormalizeUsername(string value) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Username is required.", nameof(value)) : value.Trim().Normalize().ToUpperInvariant();
    private static string DomainSeparated(ProfileCredentialKind kind, string secret) => $"tuvima:{kind}:v1\n{secret}";
    private static string NewSecurityStamp() => RandomToken(24);
    private static string RandomToken(int bytes) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    private static string HashToken(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string RecoveryCode()
    {
        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(10));
        return string.Join('-', Enumerable.Range(0, 4).Select(index => raw.Substring(index * 5, 5)));
    }
    private static string NormalizeRecoveryCode(string code) => code.Replace("-", string.Empty, StringComparison.Ordinal).Trim().ToUpperInvariant();
    private static void ValidatePassword(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length is < 12 or > 128) throw new ArgumentException("Password must be between 12 and 128 characters.");
    }
    private static void ValidatePin(string value)
    {
        if (value.Length is < 4 or > 12 || value.Any(character => !char.IsAsciiDigit(character)))
            throw new ArgumentException("Profile PIN must contain 4 to 12 digits.");
    }
    private static string Sanitize(string? value, int maximumLength, string fallback)
    {
        var result = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return result.Length <= maximumLength ? result : result[..maximumLength];
    }
    private static string SanitizeReason(string value) => Sanitize(value, 100, "revoked");
}
