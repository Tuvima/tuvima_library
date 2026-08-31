using Dapper;
using MediaEngine.Api.Services.Playback;
using MediaEngine.Domain.Configuration;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Models;
using MediaEngine.Storage;
using MediaEngine.Storage.Playback;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaEngine.Api.Tests;

public sealed class AdaptiveHlsCleanupServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"tuvima_hls_cleanup_{Guid.NewGuid():N}");
    private readonly DatabaseConnection _database;
    private readonly ConfigurationDirectoryLoader _configuration;

    public AdaptiveHlsCleanupServiceTests()
    {
        Directory.CreateDirectory(_root);
        _database = new DatabaseConnection(Path.Combine(_root, "library.db"));
        _database.InitializeSchema();
        _database.RunStartupChecks();
        _configuration = new ConfigurationDirectoryLoader(Path.Combine(_root, "config"));
        _configuration.SaveCore(new CoreConfiguration { LibraryRoot = _root });
        _configuration.SaveTranscoding(new TranscodingSettings
        {
            VariantCachePath = "variants",
            VariantRetentionDays = 1,
            ShadowStorageLimitGb = 1,
            CleanupLruEnabled = true,
        });
    }

    [Fact]
    public async Task CleanupAsync_ReclaimsExpiredPackageAndDatabaseRow()
    {
        var assetId = SeedAsset();
        var packages = new AdaptiveHlsPackageRepository(_database);
        var packageRoot = Path.Combine(_root, "variants", "hls", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(packageRoot);
        await File.WriteAllTextAsync(Path.Combine(packageRoot, "master.m3u8"), "#EXTM3U");
        var package = await packages.GetOrCreateAsync(assetId, "source-hash", "test-profile", packageRoot);
        await packages.MarkReadyAsync(package.Id, packageRoot, 8);
        using (var connection = _database.CreateConnection())
        {
            connection.Execute(
                "UPDATE adaptive_hls_packages SET last_accessed = @lastAccessed WHERE id = @id;",
                new { id = package.Id, lastAccessed = DateTimeOffset.UtcNow.AddDays(-2).ToString("O") });
        }

        var hls = new AdaptiveHlsService(
            packages,
            new MediaAssetRepository(_database),
            new TextTrackRepository(_database),
            new UnusedFfmpegService(),
            _configuration,
            new TestHostApplicationLifetime(),
            NullLogger<AdaptiveHlsService>.Instance);
        var cleanup = new AdaptiveHlsCleanupService(
            packages,
            hls,
            _configuration,
            NullLogger<AdaptiveHlsCleanupService>.Instance);

        await cleanup.CleanupAsync();

        Assert.False(Directory.Exists(packageRoot));
        Assert.Null(await packages.FindByIdAsync(package.Id));
    }

    private Guid SeedAsset()
    {
        var workId = Guid.NewGuid();
        var editionId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        using var connection = _database.CreateConnection();
        connection.Execute("INSERT INTO works (id, media_type) VALUES (@workId, 'Movies');", new { workId });
        connection.Execute("INSERT INTO editions (id, work_id) VALUES (@editionId, @workId);", new { editionId, workId });
        connection.Execute("""
            INSERT INTO media_assets (id, edition_id, content_hash, file_path_root, status)
            VALUES (@assetId, @editionId, 'source-hash', @path, 'Normal');
            """, new { assetId, editionId, path = Path.Combine(_root, "source.mp4") });
        return assetId;
    }

    public void Dispose()
    {
        _configuration.Dispose();
        _database.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private sealed class UnusedFfmpegService : IFFmpegService
    {
        public string? FfmpegPath => null;
        public string? FfprobePath => null;
        public bool IsAvailable => false;
        public HardwareCapabilities HardwareCapabilities { get; } = new();
        public Task<MediaProbeResult?> ProbeAsync(string filePath, CancellationToken ct = default) =>
            Task.FromResult<MediaProbeResult?>(null);
        public Task<(int ExitCode, string Output, string Error)> RunAsync(string arguments, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class TestHostApplicationLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }
}
