using System.IO.Compression;
using Dapper;
using MediaEngine.Api.Services;
using MediaEngine.Storage;
using Microsoft.Data.Sqlite;

namespace MediaEngine.Api.Tests;

public sealed class DatabaseBackupServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"tuvima-backup-tests-{Guid.NewGuid():N}");

    public DatabaseBackupServiceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task CreateAsync_ProducesConsistentArchiveWithoutSecrets()
    {
        var config = Path.Combine(_root, "config");
        var dbPath = Path.Combine(_root, "library.db");
        Directory.CreateDirectory(Path.Combine(config, "secrets"));
        File.WriteAllText(Path.Combine(config, "core.json"), "{\"server_name\":\"Before\"}");
        File.WriteAllText(Path.Combine(config, "secrets", "tmdb.json"), "{\"api_key\":\"secret\"}");

        using var database = CreateDatabase(dbPath);
        using (var connection = database.CreateConnection())
        {
            connection.Execute("CREATE TABLE backup_probe(value TEXT NOT NULL);");
            connection.Execute("INSERT INTO backup_probe(value) VALUES ('before');");
        }

        var service = new DatabaseBackupService(database, config);
        var archivePath = await service.CreateAsync(CancellationToken.None);

        using var archive = ZipFile.OpenRead(archivePath);
        Assert.NotNull(archive.GetEntry("database/library.db"));
        Assert.NotNull(archive.GetEntry("config/core.json"));
        Assert.NotNull(archive.GetEntry("manifest.json"));
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName.StartsWith("config/secrets/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScheduledRestore_ReplacesDatabaseAndConfigOnNextStartup()
    {
        var config = Path.Combine(_root, "restore-config");
        var dbPath = Path.Combine(_root, "restore.db");
        Directory.CreateDirectory(config);
        var corePath = Path.Combine(config, "core.json");
        File.WriteAllText(corePath, "{\"server_name\":\"Before\"}");

        string archiveName;
        using (var database = CreateDatabase(dbPath))
        {
            using (var connection = database.CreateConnection())
            {
                connection.Execute("CREATE TABLE backup_probe(value TEXT NOT NULL);");
                connection.Execute("INSERT INTO backup_probe(value) VALUES ('before');");
            }

            var service = new DatabaseBackupService(database, config);
            var archivePath = await service.CreateAsync(CancellationToken.None);
            archiveName = Path.GetFileName(archivePath);

            using var changed = database.CreateConnection();
            changed.Execute("INSERT INTO backup_probe(value) VALUES ('after');");
            File.WriteAllText(corePath, "{\"server_name\":\"After\"}");
            var result = service.ScheduleRestore(archiveName);
            Assert.True(result.Scheduled);
            Assert.True(result.RestartRequired);
        }

        DatabaseBackupService.ApplyPendingRestore(config, dbPath);

        using var restored = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        restored.Open();
        Assert.Equal(["before"], restored.Query<string>("SELECT value FROM backup_probe ORDER BY rowid;").ToArray());
        Assert.Contains("Before", File.ReadAllText(corePath));
        Assert.NotEmpty(Directory.GetFiles(_root, "restore.db.pre-restore-*.bak"));
    }

    private static DatabaseConnection CreateDatabase(string path)
    {
        var database = new DatabaseConnection(path);
        database.InitializeSchema();
        database.RunStartupChecks();
        return database;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            try { Directory.Delete(_root, recursive: true); }
            catch (IOException) { }
        }
    }
}
