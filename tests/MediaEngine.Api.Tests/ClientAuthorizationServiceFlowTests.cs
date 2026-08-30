using MediaEngine.Api.Security;
using MediaEngine.Contracts.Authentication;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Storage;
using Microsoft.Data.Sqlite;

namespace MediaEngine.Api.Tests;

public sealed class ClientAuthorizationServiceFlowTests : IDisposable
{
    private readonly string _databasePath;
    private readonly DatabaseConnection _database;
    private readonly ClientAuthorizationService _authorization;

    public ClientAuthorizationServiceFlowTests()
    {
        DapperConfiguration.Configure();
        _databasePath = Path.Combine(Path.GetTempPath(), $"tuvima_client_auth_{Guid.NewGuid():N}.db");
        _database = new DatabaseConnection(_databasePath);
        _database.InitializeSchema();
        _database.RunStartupChecks();
        _authorization = new ClientAuthorizationService(
            new ClientAuthorizationRepository(_database),
            new ProfileRepository(_database),
            TimeProvider.System);
    }

    [Fact]
    public async Task PairRefreshAndRevoke_EnforcesServerIssuedDeviceIdentity()
    {
        var started = await _authorization.BeginAsync(new DeviceAuthorizationRequest
        {
            ClientId = "headless-tv-test",
            ClientName = "Headless TV test",
            ClientVersion = "1.0.0",
            DeviceName = "Living room television",
            DeviceClass = "television",
            Scope = "library.read progress.read progress.write playback.read playback.write queue.read queue.write",
            Capabilities = new ClientCapabilitiesDto
            {
                Containers = ["mp4"],
                VideoCodecs = ["h264"],
                AudioCodecs = ["aac"],
                MaxHeight = 2160,
            },
        }, "https://library.example");

        Assert.Equal("https://library.example/pair", started.VerificationUri);
        var review = Assert.IsType<PairingReviewResponse>(await _authorization.ReviewAsync(started.UserCode));
        Assert.Equal("television", review.DeviceClass);
        Assert.True(await _authorization.DecideAsync(
            new PairingDecisionRequest { UserCode = started.UserCode, Approved = true },
            Profile.SeedProfileId,
            Profile.SeedProfileId));

        var issued = await _authorization.ExchangeAsync(new OAuthTokenRequest
        {
            GrantType = ClientAuthorizationService.DeviceGrantType,
            ClientId = "headless-tv-test",
            DeviceCode = started.DeviceCode,
        });
        var token = Assert.IsType<OAuthTokenResponse>(issued.Success);
        var identity = Assert.IsType<ClientAccessIdentity>(await _authorization.ValidateAccessTokenAsync(token.AccessToken));
        Assert.Equal(token.DeviceId, identity.Device.Id);
        Assert.Equal(Profile.SeedProfileId, identity.Device.ProfileId);
        Assert.Equal("television", identity.Device.DeviceClass);
        Assert.Equal("headless-tv-test", identity.Device.ClientId);

        var refreshed = await _authorization.ExchangeAsync(new OAuthTokenRequest
        {
            GrantType = ClientAuthorizationService.RefreshGrantType,
            ClientId = "headless-tv-test",
            RefreshToken = token.RefreshToken,
        });
        var rotated = Assert.IsType<OAuthTokenResponse>(refreshed.Success);
        Assert.NotEqual(token.RefreshToken, rotated.RefreshToken);
        Assert.NotNull(await _authorization.ValidateAccessTokenAsync(rotated.AccessToken));

        Assert.True(await _authorization.RevokeDeviceAsync(token.DeviceId, Profile.SeedProfileId));
        Assert.Null(await _authorization.ValidateAccessTokenAsync(rotated.AccessToken));
    }

    public void Dispose()
    {
        _database.Dispose();
        SqliteConnection.ClearAllPools();
        File.Delete(_databasePath);
    }
}
