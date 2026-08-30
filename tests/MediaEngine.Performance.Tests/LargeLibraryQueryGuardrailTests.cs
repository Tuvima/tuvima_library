using System.Diagnostics;
using Dapper;
using Microsoft.Data.Sqlite;

namespace MediaEngine.Performance.Tests;

public sealed class LargeLibraryQueryGuardrailTests : IClassFixture<LargeLibraryFixture>
{
    private readonly LargeLibraryFixture _fixture;

    public LargeLibraryQueryGuardrailTests(LargeLibraryFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void CollectionPage_UsesCoveringIndexAtOneHundredThousandWorks()
    {
        var collectionId = _fixture.SeedCollectionItems(100_000);
        using var connection = _fixture.Database.CreateConnection();

        var plan = connection.Query<QueryPlanRow>(
            """
            EXPLAIN QUERY PLAN
            SELECT work_id
            FROM collection_items
            WHERE collection_id = @collectionId
            ORDER BY sort_order
            LIMIT 50;
            """, new { collectionId }).ToList();

        Assert.Contains(plan, row => row.Detail.Contains(
            "idx_collection_items_collection_sort",
            StringComparison.OrdinalIgnoreCase));

        var timer = Stopwatch.StartNew();
        var rows = connection.Query<Guid>(
            """
            SELECT work_id
            FROM collection_items
            WHERE collection_id = @collectionId
            ORDER BY sort_order
            LIMIT 50;
            """, new { collectionId }).AsList();
        timer.Stop();

        Assert.Equal(50, rows.Count);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2),
            $"Indexed collection page took {timer.Elapsed.TotalMilliseconds:F0} ms.");
    }

    [Fact]
    public void FilePathIndexes_UseCaseInsensitiveCollation()
    {
        using var connection = _fixture.Database.CreateConnection();
        var indexes = connection.Query<string>(
            "SELECT sql FROM sqlite_master WHERE type = 'index' AND sql IS NOT NULL;").ToList();

        Assert.Contains(indexes, sql => sql.Contains(
            "file_path_root COLLATE NOCASE",
            StringComparison.OrdinalIgnoreCase));
        Assert.Contains(indexes, sql => sql.Contains(
            "file_hash_cache(absolute_path COLLATE NOCASE)",
            StringComparison.OrdinalIgnoreCase));
        Assert.Contains(indexes, sql => sql.Contains(
            "persons (name COLLATE NOCASE)",
            StringComparison.OrdinalIgnoreCase));
    }

    private sealed class QueryPlanRow
    {
        public string Detail { get; init; } = string.Empty;
    }
}
