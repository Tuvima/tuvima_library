using MediaEngine.Storage;

namespace MediaEngine.Storage.Tests;

public sealed class LargeLibraryPersonRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseConnection _db;

    public LargeLibraryPersonRepositoryTests()
    {
        DapperConfiguration.Configure();
        _dbPath = Path.Combine(Path.GetTempPath(), $"tuvima_people_batch_{Guid.NewGuid():N}.db");
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
    public async Task ListPagedAsync_ReturnsBoundedPersonPage()
    {
        SeedPeople(120);
        var repo = new PersonRepository(_db);

        var page = await repo.ListPagedAsync(role: null, offset: 50, limit: 10);

        Assert.Equal(10, page.Count);
        Assert.Equal("Person 050", page[0].Name);
        Assert.Equal("Person 059", page[^1].Name);
    }

    [Fact]
    public async Task GetByMediaAssetsAsync_LoadsPeopleForManyAssetsInOneCall()
    {
        var assetIds = SeedWorksAssetsAndPeople(40);
        var repo = new PersonRepository(_db);

        var people = await repo.GetByMediaAssetsAsync(assetIds);

        Assert.Equal(40, people.Count);
        Assert.Contains(people, person => person.Name == "Contributor 000");
        Assert.Contains(people, person => person.Name == "Contributor 039");
    }

    private void SeedPeople(int count)
    {
        using var conn = _db.CreateConnection();
        for (var i = 0; i < count; i++)
        {
            var personId = Guid.NewGuid().ToString();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO persons (id, name, created_at) VALUES ($id, $name, $createdAt);
                INSERT INTO person_roles (person_id, role) VALUES ($id, 'Author');
                """;
            cmd.Parameters.AddWithValue("$id", personId);
            cmd.Parameters.AddWithValue("$name", $"Person {i:000}");
            cmd.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }
    }

    private IReadOnlyList<Guid> SeedWorksAssetsAndPeople(int count)
    {
        using var conn = _db.CreateConnection();
        var assetIds = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var workId = Guid.NewGuid();
            var editionId = Guid.NewGuid();
            var assetId = Guid.NewGuid();
            var personId = Guid.NewGuid();
            assetIds.Add(assetId);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO works (id, media_type) VALUES ($workId, 'Books');
                INSERT INTO editions (id, work_id) VALUES ($editionId, $workId);
                INSERT INTO media_assets (id, edition_id, content_hash, file_path_root)
                    VALUES ($assetId, $editionId, $hash, $path);
                INSERT INTO persons (id, name, created_at) VALUES ($personId, $name, $createdAt);
                INSERT INTO person_roles (person_id, role) VALUES ($personId, 'Author');
                INSERT INTO person_media_links (media_asset_id, person_id, role)
                    VALUES ($assetId, $personId, 'Author');
                """;
            cmd.Parameters.AddWithValue("$workId", workId.ToString());
            cmd.Parameters.AddWithValue("$editionId", editionId.ToString());
            cmd.Parameters.AddWithValue("$assetId", assetId.ToString());
            cmd.Parameters.AddWithValue("$personId", personId.ToString());
            cmd.Parameters.AddWithValue("$hash", $"hash-{i:000}");
            cmd.Parameters.AddWithValue("$path", $"C:/library/file-{i:000}.epub");
            cmd.Parameters.AddWithValue("$name", $"Contributor {i:000}");
            cmd.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }

        return assetIds;
    }
}
