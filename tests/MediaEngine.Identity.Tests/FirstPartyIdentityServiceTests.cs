using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Identity.Contracts;
using MediaEngine.Storage;
using Microsoft.AspNetCore.Identity;

namespace MediaEngine.Identity.Tests;

public sealed class FirstPartyIdentityServiceTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"tuvima_identity_{Guid.NewGuid():N}.db");
    private readonly DatabaseConnection _database;
    private readonly IdentityRepository _identities;
    private readonly ProfileRepository _profiles;
    private readonly FirstPartyIdentityService _service;

    public FirstPartyIdentityServiceTests()
    {
        _database = new DatabaseConnection(_databasePath);
        _database.InitializeSchema();
        _database.RunStartupChecks();
        _identities = new IdentityRepository(_database);
        _profiles = new ProfileRepository(_database);
        _service = new FirstPartyIdentityService(
            _identities,
            _profiles,
            new PasswordHasher<ProfileCredential>(),
            TimeProvider.System);
    }

    [Fact]
    public async Task BootstrapAndPasswordLogin_UseWorkFactoredHashAndRevocableDeviceSession()
    {
        var bootstrap = await _service.BootstrapAdministratorAsync(
            "owner", "correct horse battery staple", "Owner", "browser-1", "Living room", "Dashboard");

        var credential = await _identities.GetCredentialByUsernameAsync("OWNER");
        Assert.NotNull(credential);
        Assert.Equal("aspnet-pbkdf2-v3", credential.HashScheme);
        Assert.DoesNotContain("correct horse battery staple", credential.SecretHash, StringComparison.Ordinal);
        Assert.NotEqual(64, credential.SecretHash.Length);
        Assert.Equal(10, bootstrap.RecoveryCodes.Count);

        var login = await _service.AuthenticatePasswordAsync(
            "OWNER", "correct horse battery staple", "browser-2", "Office", "Dashboard");
        Assert.True(login.Succeeded);
        Assert.NotNull(login.IssuedSession);
        Assert.NotNull(await _service.ValidateSessionAsync(login.IssuedSession.PlaintextToken));

        Assert.True(await _service.RevokeSessionAsync(login.IssuedSession.Session.Id, "test"));
        Assert.Null(await _service.ValidateSessionAsync(login.IssuedSession.PlaintextToken));
    }

    [Fact]
    public async Task PasswordFailures_LockCredentialAfterFiveAttempts()
    {
        await _service.BootstrapAdministratorAsync(
            "owner", "correct horse battery staple", "Owner", "browser-1", "Living room", "Dashboard");

        AuthenticationAttemptResult? attempt = null;
        for (var index = 0; index < 5; index++)
        {
            attempt = await _service.AuthenticatePasswordAsync(
                "owner", "wrong password", $"browser-{index}", "Unknown", "Dashboard");
        }

        Assert.NotNull(attempt);
        Assert.False(attempt.Succeeded);
        Assert.True(attempt.LockedOut);

        var correctWhileLocked = await _service.AuthenticatePasswordAsync(
            "owner", "correct horse battery staple", "browser-6", "Office", "Dashboard");
        Assert.False(correctWhileLocked.Succeeded);
        Assert.True(correctWhileLocked.LockedOut);
    }

    [Fact]
    public async Task ProfilePin_IsDistinctFromAdministratorPasswordAndCanBeRemoved()
    {
        await _service.BootstrapAdministratorAsync(
            "owner", "correct horse battery staple", "Owner", "browser-1", "Living room", "Dashboard");
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
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best-effort test cleanup.
        }
    }
}
