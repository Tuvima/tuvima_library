using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Identity.Contracts;
using MediaEngine.Storage;
using Microsoft.AspNetCore.Identity;
using System.Security.Cryptography;
using System.Text;

namespace MediaEngine.Identity.Tests;

public sealed class FirstPartyIdentityServiceTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"tuvima_identity_{Guid.NewGuid():N}.db");
    private readonly DatabaseConnection _database;
    private readonly IdentityRepository _identities;
    private readonly AccountRepository _accounts;
    private readonly ProfileRepository _profiles;
    private readonly FirstPartyIdentityService _service;
    private readonly ManualTimeProvider _clock = new(new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero));

    public FirstPartyIdentityServiceTests()
    {
        _database = new DatabaseConnection(_databasePath);
        _database.InitializeSchema();
        _database.RunStartupChecks();
        _identities = new IdentityRepository(_database);
        _accounts = new AccountRepository(_database);
        _profiles = new ProfileRepository(_database);
        _service = new FirstPartyIdentityService(
            _identities,
            _accounts,
            _profiles,
            new PasswordHasher<AccountCredential>(),
            new PasswordHasher<ProfileCredential>(),
            _clock);
    }

    [Fact]
    public async Task BootstrapAndPasswordLogin_UseWorkFactoredHashAndRevocableDeviceSession()
    {
        var bootstrap = await _service.BootstrapAdministratorAsync(
            "owner@example.com", "correct horse battery staple", "Owner", "browser-1", "Living room", "Dashboard");

        var account = await _accounts.GetByNormalizedEmailAsync("OWNER@EXAMPLE.COM");
        var credential = await _identities.GetAccountCredentialAsync(account!.Id, AccountCredentialKind.Password);
        Assert.NotNull(credential);
        Assert.Equal("aspnet-pbkdf2-v3", credential.HashScheme);
        Assert.DoesNotContain("correct horse battery staple", credential.SecretHash, StringComparison.Ordinal);
        Assert.NotEqual(64, credential.SecretHash.Length);
        Assert.Equal(10, bootstrap.RecoveryCodes.Count);
        Assert.Equal("owner@example.com", account.Email);
        Assert.Equal("Owner", bootstrap.Profile.DisplayName);

        var login = await _service.AuthenticatePasswordAsync(
            "OWNER@example.com", "correct horse battery staple", "browser-2", "Office", "Dashboard");
        Assert.True(login.Succeeded);
        Assert.NotNull(login.IssuedSession);
        Assert.NotNull(await _service.ValidateSessionAsync(login.IssuedSession.PlaintextToken));

        Assert.True(await _service.RevokeSessionAsync(login.IssuedSession.Session.Id, "test"));
        Assert.Null(await _service.ValidateSessionAsync(login.IssuedSession.PlaintextToken));
    }

    [Fact]
    public async Task SessionCreatedByLogin_CanBeReadBackByItsGuid()
    {
        var bootstrap = await _service.BootstrapAdministratorAsync(
            "owner@example.com", "correct horse battery staple", "Owner", "browser-1", "Living room", "Dashboard");

        var stored = await _identities.GetSessionByIdAsync(bootstrap.Session.Id);

        Assert.NotNull(stored);
        Assert.Equal(bootstrap.Session.Id, stored.Id);
        Assert.Equal(bootstrap.Session.AccountId, stored.AccountId);
    }

    [Fact]
    public async Task PasswordFailures_LockCredentialAfterFiveAttempts()
    {
        await _service.BootstrapAdministratorAsync(
            "owner@example.com", "correct horse battery staple", "Owner", "browser-1", "Living room", "Dashboard");

        AuthenticationAttemptResult? attempt = null;
        for (var index = 0; index < 5; index++)
        {
            attempt = await _service.AuthenticatePasswordAsync(
                "owner@example.com", "wrong password", $"browser-{index}", "Unknown", "Dashboard");
        }

        Assert.NotNull(attempt);
        Assert.False(attempt.Succeeded);
        Assert.True(attempt.LockedOut);

        var correctWhileLocked = await _service.AuthenticatePasswordAsync(
            "owner@example.com", "correct horse battery staple", "browser-6", "Office", "Dashboard");
        Assert.False(correctWhileLocked.Succeeded);
        Assert.True(correctWhileLocked.LockedOut);
    }

    [Fact]
    public async Task ProfilePin_IsDistinctFromAdministratorPasswordAndCanBeRemoved()
    {
        await _service.BootstrapAdministratorAsync(
            "owner@example.com", "correct horse battery staple", "Owner", "browser-1", "Living room", "Dashboard");
        var profile = new Profile
        {
            Id = Guid.NewGuid(),
            DisplayName = "Kids",
            AvatarColor = "#7C4DFF",
            Role = ProfileRole.RestrictedProfile,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await _profiles.InsertAsync(profile);

        await _service.SetProfilePinAsync(profile.Id, "2468");
        var pinLogin = await _service.AuthenticatePinAsync(
            profile.Id, "2468", "tablet", "Kids tablet", "Dashboard");
        Assert.True(pinLogin.Succeeded);
        Assert.Equal("ProfilePin", pinLogin.IssuedSession!.Session.AuthenticationMethod);

        await _service.SetProfilePinAsync(profile.Id, null);
        Assert.Null(await _identities.GetCredentialAsync(profile.Id, ProfileCredentialKind.ProfilePin));
        Assert.Null(await _service.ValidateSessionAsync(pinLogin.IssuedSession.PlaintextToken));
    }

    [Fact]
    public async Task BootstrapAdministrator_BlankDisplayNameDefaultsToAdministrator()
    {
        var bootstrap = await _service.BootstrapAdministratorAsync(
            "administrator@example.com", "correct horse battery staple", "", "browser-1", "Server", "Dashboard");

        Assert.Equal("Administrator", bootstrap.Profile.DisplayName);
    }

    [Fact]
    public async Task SwitchActiveProfileAsync_ReportsPinRequirementSeparatelyFromAccessDenial()
    {
        var bootstrap = await _service.BootstrapAdministratorAsync(
            "owner@example.com", "correct horse battery staple", "Owner", "browser-1", "Living room", "Dashboard");
        var profile = new Profile
        {
            Id = Guid.NewGuid(),
            DisplayName = "Kids",
            AvatarColor = "#7C4DFF",
            Role = ProfileRole.RestrictedProfile,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await _profiles.InsertAsync(profile);
        await _accounts.GrantProfileAsync(new AccountProfileGrant
        {
            AccountId = bootstrap.Account.Id,
            ProfileId = profile.Id,
            GrantedAt = DateTimeOffset.UtcNow,
        });
        await _service.SetProfilePinAsync(profile.Id, "2468");

        await Assert.ThrowsAsync<ProfilePinRequiredException>(() =>
            _service.SwitchActiveProfileAsync(bootstrap.PlaintextToken, profile.Id, null));

        var switched = await _service.SwitchActiveProfileAsync(
            bootstrap.PlaintextToken, profile.Id, "2468");
        Assert.Equal(profile.Id, switched.ActiveProfile.Id);
    }

    [Fact]
    public async Task LocalPassword_AllowsEightCharacters()
    {
        var bootstrap = await _service.BootstrapAdministratorAsync(
            "administrator@example.com", "12345678", "Administrator", "browser-1", "Server", "Dashboard");

        Assert.Equal("Administrator", bootstrap.Profile.DisplayName);
    }

    [Fact]
    public async Task LocalPassword_RejectsFewerThanEightCharacters()
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.BootstrapAdministratorAsync(
            "administrator@example.com", "1234567", "Administrator", "browser-1", "Server", "Dashboard"));

        Assert.Contains("between 8 and 128 characters", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HostAdministratorPasswordReset_RevokesSessionsClearsLockoutAndRotatesRecoveryCodes()
    {
        var bootstrap = await _service.BootstrapAdministratorAsync(
            "administrator@example.com", "correct horse battery staple", "Administrator", "browser-1", "Server", "Dashboard");
        var previousRecoveryCode = bootstrap.RecoveryCodes[0];

        for (var index = 0; index < 5; index++)
        {
            await _service.AuthenticatePasswordAsync(
                "administrator@example.com", "wrong password", $"browser-{index + 2}", "Unknown", "Dashboard");
        }

        var replacementCodes = await _service.ResetAdministratorPasswordFromHostAsync(
            "administrator@example.com", "a newer correct horse battery staple");

        Assert.Equal(10, replacementCodes.Count);
        Assert.DoesNotContain(previousRecoveryCode, replacementCodes);
        Assert.Null(await _service.ValidateSessionAsync(bootstrap.PlaintextToken));

        var oldPassword = await _service.AuthenticatePasswordAsync(
            "administrator@example.com", "correct horse battery staple", "browser-8", "Office", "Dashboard");
        Assert.False(oldPassword.Succeeded);
        Assert.False(oldPassword.LockedOut);

        var newPassword = await _service.AuthenticatePasswordAsync(
            "administrator@example.com", "a newer correct horse battery staple", "browser-9", "Office", "Dashboard");
        Assert.True(newPassword.Succeeded);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.ResetPasswordWithRecoveryCodeAsync(
                "administrator@example.com",
                previousRecoveryCode,
                "yet another correct horse battery staple"));
    }

    [Fact]
    public async Task HostAdministratorPasswordReset_RejectsUnknownEmail()
    {
        await _service.BootstrapAdministratorAsync(
            "administrator@example.com", "correct horse battery staple", "Administrator", "browser-1", "Server", "Dashboard");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.ResetAdministratorPasswordFromHostAsync(
                "somebody-else@example.com",
            "a newer correct horse battery staple"));
    }

    [Fact]
    public async Task AccountProfileGrants_ControlSwitchingAndSwitchingClearsElevation()
    {
        var bootstrap = await _service.BootstrapAdministratorAsync(
            "owner@example.com", "correct horse battery staple", "Dad", "browser-1", "Server", "Dashboard");
        await _service.SetAdministratorPinAsync(bootstrap.Profile.Id, "2468");
        var elevated = await _service.ElevateAdministratorAsync(bootstrap.PlaintextToken, "2468");
        Assert.True(elevated.Succeeded);

        var child = new Profile
        {
            Id = Guid.NewGuid(), DisplayName = "Child", AvatarColor = "#123456",
            Role = ProfileRole.RestrictedProfile, CreatedAt = _clock.GetUtcNow(),
        };
        var ungranted = new Profile
        {
            Id = Guid.NewGuid(), DisplayName = "Guest", AvatarColor = "#654321",
            Role = ProfileRole.StandardUser, CreatedAt = _clock.GetUtcNow(),
        };
        await _profiles.InsertAsync(child);
        await _profiles.InsertAsync(ungranted);
        await _accounts.GrantProfileAsync(new AccountProfileGrant
        {
            AccountId = bootstrap.Account.Id, ProfileId = child.Id, GrantedAt = _clock.GetUtcNow(),
        });

        await _service.SwitchActiveProfileAsync(bootstrap.PlaintextToken, child.Id, null);
        await _service.SwitchActiveProfileAsync(bootstrap.PlaintextToken, bootstrap.Profile.Id, null);
        Assert.Null(await _service.GetAdministratorElevationAsync(bootstrap.PlaintextToken));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.SwitchActiveProfileAsync(bootstrap.PlaintextToken, ungranted.Id, null));
    }

    [Fact]
    public async Task AdministratorElevation_ExpiresAfterThirtyMinutes()
    {
        var bootstrap = await _service.BootstrapAdministratorAsync(
            "owner@example.com", "correct horse battery staple", "Dad", "browser-1", "Server", "Dashboard");

        var elevated = await _service.ElevateAdministratorAsync(
            bootstrap.PlaintextToken, "correct horse battery staple");
        Assert.True(elevated.Succeeded);
        Assert.Equal(_clock.GetUtcNow().AddMinutes(30), await _service.GetAdministratorElevationAsync(bootstrap.PlaintextToken));

        _clock.Advance(TimeSpan.FromMinutes(31));
        Assert.Null(await _service.GetAdministratorElevationAsync(bootstrap.PlaintextToken));
    }

    [Fact]
    public async Task PasswordResetToken_IsSingleUseAndRevokesExistingSessions()
    {
        var bootstrap = await _service.BootstrapAdministratorAsync(
            "owner@example.com", "correct horse battery staple", "Owner", "browser-1", "Server", "Dashboard");
        var token = await _service.BeginPasswordResetAsync("OWNER@example.com");
        Assert.NotNull(token);
        Assert.Null(await _service.BeginPasswordResetAsync("unknown@example.com"));

        await _service.ResetPasswordWithTokenAsync(token!, "replacement password");
        Assert.Null(await _service.ValidateSessionAsync(bootstrap.PlaintextToken));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.ResetPasswordWithTokenAsync(token!, "another replacement"));
        Assert.True((await _service.AuthenticatePasswordAsync(
            "owner@example.com", "replacement password", "browser-2", "Office", "Dashboard")).Succeeded);
    }

    [Fact]
    public async Task RecoveryCodeRegeneration_RequiresPasswordAndInvalidatesPreviousCodes()
    {
        var bootstrap = await _service.BootstrapAdministratorAsync(
            "owner@example.com", "correct horse battery staple", "Owner", "browser-1", "Server", "Dashboard");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.RegenerateRecoveryCodesAsync(bootstrap.Account.Id, "wrong password"));

        var replacement = await _service.RegenerateRecoveryCodesAsync(
            bootstrap.Account.Id, "correct horse battery staple");
        Assert.Equal(10, replacement.Count);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.ResetPasswordWithRecoveryCodeAsync(
                "owner@example.com", bootstrap.RecoveryCodes[0], "replacement password"));
    }

    [Fact]
    public async Task Invitation_IsSingleUseAndCreatesAccountPasswordSession()
    {
        var bootstrap = await _service.BootstrapAdministratorAsync(
            "owner@example.com", "correct horse battery staple", "Owner", "browser-1", "Server", "Dashboard");
        const string token = "one-time-invitation-token";
        var invited = new Account
        {
            Id = Guid.NewGuid(), Email = "family@example.com", NormalizedEmail = "FAMILY@EXAMPLE.COM",
            IsEnabled = true, CreatedAt = _clock.GetUtcNow(), UpdatedAt = _clock.GetUtcNow(),
        };
        await _accounts.InsertAsync(invited);
        await _accounts.GrantProfileAsync(new AccountProfileGrant
        {
            AccountId = invited.Id, ProfileId = bootstrap.Profile.Id, IsDefault = true, GrantedAt = _clock.GetUtcNow(),
        });
        await _accounts.InsertInvitationAsync(new AccountInvitation
        {
            Id = Guid.NewGuid(), AccountId = invited.Id, TokenHash = HashToken(token),
            CreatedAt = _clock.GetUtcNow(), ExpiresAt = _clock.GetUtcNow().AddDays(7),
        });

        var accepted = await _service.AcceptInvitationAsync(
            token, "family password", "remote-browser", "Family laptop", "Dashboard");
        Assert.Equal(invited.Id, accepted.Account.Id);
        Assert.NotNull(await _service.ValidateSessionAsync(accepted.PlaintextToken));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.AcceptInvitationAsync(token, "different password", "other", "Other", "Dashboard"));
    }

    public void Dispose()
    {
        _database.Dispose();
        TryDelete(_databasePath);
        TryDelete($"{_databasePath}-wal");
        TryDelete($"{_databasePath}-shm");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort test cleanup.
        }
    }

    private static string HashToken(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
