using MediaEngine.Api.Services.Playback;
using MediaEngine.Storage;
using Microsoft.AspNetCore.DataProtection;

namespace MediaEngine.Api.Tests;

public sealed class HlsAccessGrantServiceTests
{
    [Fact]
    public void Grant_IsScopedToPackageAndExpires()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero));
        var service = new HlsAccessGrantService(
            new EphemeralDataProtectionProvider(),
            new ConfigurationDirectoryLoader(Path.Combine(FindRepoRoot(), "config")),
            clock);
        var assetId = Guid.NewGuid();
        var packageId = Guid.NewGuid();

        var grant = service.Create(assetId, packageId);

        Assert.True(service.TryValidate(grant.Value, packageId, out var grantedAssetId));
        Assert.Equal(assetId, grantedAssetId);
        Assert.False(service.TryValidate(grant.Value, Guid.NewGuid(), out _));

        clock.Advance(TimeSpan.FromHours(5));
        Assert.False(service.TryValidate(grant.Value, packageId, out _));
    }

    [Fact]
    public void Grant_RejectsTampering()
    {
        var service = new HlsAccessGrantService(
            new EphemeralDataProtectionProvider(),
            new ConfigurationDirectoryLoader(Path.Combine(FindRepoRoot(), "config")));
        var packageId = Guid.NewGuid();
        var grant = service.Create(Guid.NewGuid(), packageId);
        var replacement = grant.Value[^1] == 'a' ? 'b' : 'a';
        var tampered = grant.Value[..^1] + replacement;

        Assert.False(service.TryValidate(tampered, packageId, out _));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MediaEngine.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan duration) => _utcNow += duration;
    }
}
