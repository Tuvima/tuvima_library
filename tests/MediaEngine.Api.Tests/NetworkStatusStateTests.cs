using MediaEngine.Api.Services.Networking;

namespace MediaEngine.Api.Tests;

public sealed class NetworkStatusStateTests
{
    [Fact]
    public void ActiveLeaseBecomesExpiredWhenItsDeadlinePasses()
    {
        var now = DateTimeOffset.Parse("2026-08-27T12:00:00Z");
        var mapping = new RouterMappingResult(
            RouterMappingState.Active,
            "PCP",
            "Mapped.",
            443,
            now.AddSeconds(-1));

        Assert.Equal(RouterMappingState.Expired, NetworkStatusService.EffectiveMappingState(mapping, now));
    }

    [Fact]
    public void MissingMappingIsNotAttemptedAndFutureLeaseRemainsActive()
    {
        var now = DateTimeOffset.Parse("2026-08-27T12:00:00Z");
        Assert.Equal(RouterMappingState.NotAttempted, NetworkStatusService.EffectiveMappingState(null, now));
        Assert.Equal(
            RouterMappingState.Active,
            NetworkStatusService.EffectiveMappingState(
                new RouterMappingResult(RouterMappingState.Active, "UPnP", "Mapped.", ExpiresAt: now.AddMinutes(1)),
                now));
    }
}
