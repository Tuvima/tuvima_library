using Dapper;
using MediaEngine.Domain;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Providers.Services;
using MediaEngine.Providers.Workers;
using MediaEngine.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaEngine.Providers.Tests;

public sealed class FictionalEntityWorkerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly DatabaseConnection _db;
    private readonly WorkRepository _workRepo;
    private readonly MediaAssetRepository _assetRepo;
    private readonly CanonicalValueRepository _canonicalRepo;
    private readonly FictionalEntityRepository _fictionalEntityRepo;

    public FictionalEntityWorkerTests()
    {
        DapperConfiguration.Configure();

        _tempRoot = Path.Combine(Path.GetTempPath(), $"tuvima_stage3_worker_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);

        _db = new DatabaseConnection(Path.Combine(_tempRoot, "library.db"));
        _db.InitializeSchema();
        _db.RunStartupChecks();

        _workRepo = new WorkRepository(_db);
        _assetRepo = new MediaAssetRepository(_db);
        _canonicalRepo = new CanonicalValueRepository(_db);
        _fictionalEntityRepo = new FictionalEntityRepository(_db);
    }

    public void Dispose()
    {
        try { _db.Dispose(); } catch { }
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    [Fact]
    public async Task EnrichAsync_UsesParentWorkCanonicals_ToCreateEntitiesAndWorkLinks()
    {
        var showWorkId = await _workRepo.InsertParentAsync(MediaType.TV, "tv:the-expanse", null, null);
        var seasonWorkId = await _workRepo.InsertChildAsync(MediaType.TV, showWorkId, 1);
        var episodeWorkId = await _workRepo.InsertChildAsync(MediaType.TV, seasonWorkId, 2);
        var assetId = await SeedAssetForExistingWorkAsync(episodeWorkId, Path.Combine("TV", "The Expanse", "Season 01", "The Expanse - S01E02.mkv"));

        await SeedCanonicalsAsync(
            showWorkId,
            (MetadataFieldConstants.Title, "The Expanse"),
            ("wikidata_qid", "QSHOW"),
            (MetadataFieldConstants.FictionalUniverse, "The Expanse universe"),
            ("fictional_universe_qid", "QUNIVERSE::The Expanse universe"),
            (MetadataFieldConstants.Characters, "James Holden|||Chrisjen Avasarala"),
            ("characters_qid", "QCHAR1::James Holden|||QCHAR2::Chrisjen Avasarala"),
            (MetadataFieldConstants.NarrativeLocation, "Rocinante"),
            ("narrative_location_qid", "QLOC1::Rocinante"));

        var harvesting = new RecordingMetadataHarvestingService();
        var resolver = new NarrativeRootResolver(
            _canonicalRepo,
            new NarrativeRootRepository(_db),
            new QidLabelRepository(_db),
            new SystemActivityRepository(_db),
            NullLogger<NarrativeRootResolver>.Instance);
        var recursiveService = new RecursiveFictionalEntityService(
            _fictionalEntityRepo,
            harvesting,
            NullLogger<RecursiveFictionalEntityService>.Instance);
        var worker = new FictionalEntityWorker(
            resolver,
            recursiveService,
            _canonicalRepo,
            _workRepo,
            NullLogger<FictionalEntityWorker>.Instance);

        await worker.EnrichAsync(assetId, "QSHOW", CancellationToken.None);

        Assert.Equal(3, await _fictionalEntityRepo.CountAsync());

        var linkedEntities = await _fictionalEntityRepo.GetByWorkQidAsync("QSHOW");
        Assert.Equal(3, linkedEntities.Count);
        Assert.Contains(linkedEntities, entity => entity.WikidataQid == "QCHAR1");
        Assert.Contains(linkedEntities, entity => entity.WikidataQid == "QCHAR2");
        Assert.Contains(linkedEntities, entity => entity.WikidataQid == "QLOC1");

        Assert.Equal(3, harvesting.Requests.Count);
        Assert.All(harvesting.Requests, request => Assert.Equal(MediaType.Unknown, request.MediaType));
    }

    private async Task<Guid> SeedAssetForExistingWorkAsync(Guid workId, string relativeFilePath)
    {
        var editionId = Guid.NewGuid();
        var filePath = Path.Combine(_tempRoot, relativeFilePath);
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

    private sealed class RecordingMetadataHarvestingService : IMetadataHarvestingService
    {
        public List<HarvestRequest> Requests { get; } = [];

        public int PendingCount => Requests.Count;

        public ValueTask EnqueueAsync(HarvestRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return ValueTask.CompletedTask;
        }

        public Task ProcessSynchronousAsync(HarvestRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
    }
}
