using System.Net;
using Dapper;
using MediaEngine.Domain;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
using MediaEngine.Providers.Workers;
using MediaEngine.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace MediaEngine.Providers.Tests;

public sealed class CoverArtWorkerCentralStorageTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _libraryRoot;
    private readonly DatabaseConnection _db;
    private readonly MediaAssetRepository _assetRepo;
    private readonly CanonicalValueRepository _canonicalRepo;
    private readonly WorkRepository _workRepo;
    private readonly EntityAssetRepository _entityAssetRepo;
    private readonly AssetPathService _assetPaths;

    public CoverArtWorkerCentralStorageTests()
    {
        DapperConfiguration.Configure();

        _tempRoot = Path.Combine(Path.GetTempPath(), $"tuvima_cover_worker_{Guid.NewGuid():N}");
        _libraryRoot = Path.Combine(_tempRoot, "library");
        Directory.CreateDirectory(_libraryRoot);

        _db = new DatabaseConnection(Path.Combine(_tempRoot, "library.db"));
        _db.InitializeSchema();
        _db.RunStartupChecks();

        _assetRepo = new MediaAssetRepository(_db);
        _canonicalRepo = new CanonicalValueRepository(_db);
        _workRepo = new WorkRepository(_db);
        _entityAssetRepo = new EntityAssetRepository(_db);
        _assetPaths = new AssetPathService(_libraryRoot);
    }

    public void Dispose()
    {
        try { _db.Dispose(); } catch { }
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    [Fact]
    public async Task DownloadAndPersistAsync_WritesCentralCoverThumbAndHero()
    {
        var workId = await _workRepo.InsertStandaloneAsync(MediaType.Movies);
        var assetId = await SeedAssetForExistingWorkAsync(workId, Path.Combine("Movies", "Arrival (2016).mkv"));
        await SeedCanonicalsAsync(
            workId,
            ("cover", "https://images.test/poster.jpg"),
            ("title", "Arrival"),
            ("wikidata_qid", "QMOVIE"));

        var worker = new CoverArtWorker(
            _assetRepo,
            _canonicalRepo,
            _workRepo,
            new NoOpImageCacheRepository(),
            new WritingHeroBannerGenerator(),
            new RoutingHttpClientFactory(_ => ImageResponse(CreateTestImageBytes())),
            _assetPaths,
            NullLogger<CoverArtWorker>.Instance,
            assetExportService: null,
            coverArtHash: null,
            entityAssetRepo: _entityAssetRepo);

        await worker.DownloadAndPersistAsync(assetId, null, CancellationToken.None);

        var lineage = await _workRepo.GetLineageByAssetAsync(assetId);
        var ownerEntityId = lineage?.TargetForParentScope ?? assetId;
        var coverAsset = Assert.Single(await _entityAssetRepo.GetByEntityAsync(ownerEntityId.ToString(), "CoverArt"));

        Assert.True(File.Exists(coverAsset.LocalImagePath));
        Assert.True(File.Exists(_assetPaths.GetCentralDerivedPath("Work", ownerEntityId, "thumb", "cover-thumb.jpg")));
        Assert.True(File.Exists(_assetPaths.GetCentralDerivedPath("Work", ownerEntityId, "hero", "hero.jpg")));
    }

    private async Task<Guid> SeedAssetForExistingWorkAsync(Guid workId, string relativeFilePath)
    {
        var editionId = Guid.NewGuid();
        var filePath = Path.Combine(_libraryRoot, relativeFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllBytesAsync(filePath, [0, 1, 2]);

        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            "INSERT INTO editions (id, work_id) VALUES (@EditionId, @WorkId);",
            new
            {
                EditionId = editionId.ToString(),
                WorkId = workId.ToString(),
            });

        var assetId = Guid.NewGuid();
        await _assetRepo.InsertAsync(new MediaAsset
        {
            Id = assetId,
            EditionId = editionId,
            ContentHash = $"hash_{assetId:N}",
            FilePathRoot = filePath,
            Status = AssetStatus.Normal,
        });

        return assetId;
    }

    private Task SeedCanonicalsAsync(Guid entityId, params (string Key, string Value)[] values)
    {
        return _canonicalRepo.UpsertBatchAsync(values
            .Select(value => new CanonicalValue
            {
                EntityId = entityId,
                Key = value.Key,
                Value = value.Value,
                LastScoredAt = DateTimeOffset.UtcNow,
            })
            .ToList());
    }

    private static HttpResponseMessage ImageResponse(byte[] bytes) =>
        new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes),
        };

    private static byte[] CreateTestImageBytes()
    {
        using var bitmap = new SKBitmap(16, 16);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.DarkOrange);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }

    private sealed class NoOpImageCacheRepository : IImageCacheRepository
    {
        public Task<string?> FindByHashAsync(string contentHash, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task InsertAsync(string contentHash, string filePath, string? sourceUrl = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> IsUserOverrideAsync(string contentHash, CancellationToken ct = default) => Task.FromResult(false);
        public Task<string?> FindBySourceUrlAsync(string sourceUrl, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task SetUserOverrideAsync(string contentHash, bool isOverride, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetPerceptualHashAsync(string contentHash, ulong phash, CancellationToken ct = default) => Task.CompletedTask;
        public Task<ulong?> GetPerceptualHashAsync(string contentHash, CancellationToken ct = default) => Task.FromResult<ulong?>(null);
    }

    private sealed class WritingHeroBannerGenerator : IHeroBannerGenerator
    {
        public Task<HeroBannerResult> GenerateAsync(string coverImagePath, string outputDirectory, CancellationToken ct = default)
        {
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllBytes(Path.Combine(outputDirectory, "hero.jpg"), [7, 8, 9]);
            return Task.FromResult(new HeroBannerResult("hero.jpg", "#000000", false));
        }
    }

    private sealed class RoutingHttpClientFactory : IHttpClientFactory
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public RoutingHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        public HttpClient CreateClient(string name)
            => new(new RoutingHttpMessageHandler(_responder), disposeHandler: true);
    }

    private sealed class RoutingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public RoutingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }
}
