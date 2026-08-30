using Dapper;
using MediaEngine.Storage;
using Microsoft.Data.Sqlite;

namespace MediaEngine.Performance.Tests;

public sealed class LargeLibraryFixture : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(),
        $"tuvima_performance_{Guid.NewGuid():N}.db");

    public LargeLibraryFixture()
    {
        Database = new DatabaseConnection(_path);
        Database.InitializeSchema();
        Database.RunStartupChecks();
    }

    public DatabaseConnection Database { get; }

    public Guid SeedCollectionItems(int workCount)
    {
        var collectionId = Guid.NewGuid();
        using var connection = Database.CreateConnection();
        using var transaction = connection.BeginTransaction();
        connection.Execute(
            """
            INSERT INTO collections (id, display_name, collection_type)
            VALUES (@collectionId, 'Performance fixture', 'Custom');

            WITH RECURSIVE sequence(value) AS (
                SELECT 1
                UNION ALL
                SELECT value + 1 FROM sequence WHERE value < @workCount
            )
            INSERT INTO works (id, media_type, curator_state)
            SELECT randomblob(16),
                   CASE value % 4
                       WHEN 0 THEN 'Books'
                       WHEN 1 THEN 'Movies'
                       WHEN 2 THEN 'Music'
                       ELSE 'Audiobooks'
                   END,
                   'accepted'
            FROM sequence;

            INSERT INTO collection_items (id, collection_id, work_id, sort_order)
            SELECT randomblob(16), @collectionId, id, rowid
            FROM works;
            """,
            new { collectionId, workCount },
            transaction,
            commandTimeout: 120);
        transaction.Commit();
        return collectionId;
    }

    public void Dispose()
    {
        Database.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path))
            File.Delete(_path);
    }
}
