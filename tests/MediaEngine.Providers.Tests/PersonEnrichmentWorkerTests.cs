using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Models;
using MediaEngine.Providers.Workers;
using MediaEngine.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaEngine.Providers.Tests;

public sealed class PersonEnrichmentWorkerTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseConnection _db;

    public PersonEnrichmentWorkerTests()
    {
        DapperConfiguration.Configure();
        _dbPath = Path.Combine(Path.GetTempPath(), $"tuvima_person_enrichment_{Guid.NewGuid():N}.db");
        _db = new DatabaseConnection(_dbPath);
        _db.InitializeSchema();
        _db.RunStartupChecks();
    }

    public void Dispose()
    {
        try { _db.Dispose(); } catch { }
        try { File.Delete(_dbPath); } catch { }
    }

    [Fact]
    public async Task EnrichActorCharacterMappingsAsync_LinksAlignedCastAndCharacterCanonicalArrays()
    {
        var workId = Guid.NewGuid();
        var editionId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var workQid = "Q172241";
        var personRepo = new PersonRepository(_db);
        var fictionalRepo = new FictionalEntityRepository(_db);
        var canonicalRepo = new CanonicalValueRepository(_db);
        var arrayRepo = new CanonicalValueArrayRepository(_db);

        var person = await personRepo.CreateAsync(new Person
        {
            Name = "Tim Robbins",
            WikidataQid = "Q95048",
            Roles = ["Actor"],
        });

        InsertOwnedMovie(workId, editionId, assetId);

        await arrayRepo.SetValuesAsync(workId, "cast_member",
        [
            new CanonicalArrayEntry
            {
                Ordinal = 0,
                Value = "Tim Robbins",
                ValueQid = "Q95048",
            },
        ]);
        await arrayRepo.SetValuesAsync(workId, "characters",
        [
            new CanonicalArrayEntry
            {
                Ordinal = 0,
                Value = "Andy Dufresne",
                ValueQid = "Q56240620",
            },
        ]);

        var worker = new PersonEnrichmentWorker(
            new MetadataClaimRepository(_db),
            canonicalRepo,
            new StubRecursiveIdentityService(),
            new StubHarvestingService(),
            personRepo,
            fictionalRepo,
            new CollectionRepository(_db),
            NullLogger<PersonEnrichmentWorker>.Instance,
            canonicalArrayRepo: arrayRepo);

        await worker.EnrichActorCharacterMappingsAsync(assetId, workQid, CancellationToken.None);

        var character = await fictionalRepo.FindByQidAsync("Q56240620");
        Assert.NotNull(character);
        Assert.Equal("Andy Dufresne", character!.Label);

        var links = await personRepo.GetCharacterLinksAsync(person.Id);
        var link = Assert.Single(links);
        Assert.Equal(character.Id, link.FictionalEntityId);
        Assert.Equal(workQid, link.WorkQid);
    }

    private void InsertOwnedMovie(Guid workId, Guid editionId, Guid assetId)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO works (id, media_type, work_kind)
                VALUES ($workId, 'Movies', 'standalone');
            INSERT INTO editions (id, work_id)
                VALUES ($editionId, $workId);
            INSERT INTO media_assets (id, edition_id, content_hash, file_path_root)
                VALUES ($assetId, $editionId, $hash, 'C:/library/The Shawshank Redemption.mkv');
            """;
        AddGuid(cmd, "$workId", workId);
        AddGuid(cmd, "$editionId", editionId);
        AddGuid(cmd, "$assetId", assetId);
        cmd.Parameters.AddWithValue("$hash", $"asset-{assetId:N}");
        cmd.ExecuteNonQuery();
    }

    private static void AddGuid(Microsoft.Data.Sqlite.SqliteCommand command, string name, Guid value)
    {
        command.Parameters.Add(name, Microsoft.Data.Sqlite.SqliteType.Blob).Value = GuidSql.ToBlob(value);
    }

    private sealed class StubRecursiveIdentityService : IRecursiveIdentityService
    {
        public Task<IReadOnlyList<HarvestRequest>> EnrichAsync(
            Guid mediaAssetId,
            IReadOnlyList<PersonReference> persons,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<HarvestRequest>>([]);
    }

    private sealed class StubHarvestingService : IMetadataHarvestingService
    {
        public int PendingCount => 0;

        public ValueTask EnqueueAsync(HarvestRequest request, CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public Task ProcessSynchronousAsync(HarvestRequest request, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
