using MediaEngine.Api.Models;
using MediaEngine.Storage;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services;

/// <summary>
/// Normalizes legacy generated collection data so authored collection screens only
/// contain real user-managed objects. Browse-only groupings are now generated
/// dynamically and no longer require persisted System/Mix/sample collections.
/// </summary>
public static class CollectionSeeder
{
    public static Task SeedManagedCollectionsAsync(
        ICollectionRepository collectionRepo,
        IDatabaseConnection db,
        CancellationToken ct = default) =>
        RemoveLegacyGeneratedCollectionsAsync(db, ct);

    private static Task RemoveLegacyGeneratedCollectionsAsync(
        IDatabaseConnection db,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var legacyNames = string.Join(", ", BuiltInBrowseCollectionCatalog.LegacyGeneratedNames.Select(Quote));

        const string generatedTypes = "'System', 'Mix'";
        var whereClause = $"""
            collection_type IN ({generatedTypes})
            OR (profile_id IS NULL AND display_name IN ({legacyNames}))
            """;

        using var conn = db.CreateConnection();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"""
            DELETE FROM collection_items
            WHERE collection_id IN (SELECT id FROM collections WHERE {whereClause});

            DELETE FROM collection_placements
            WHERE collection_id IN (SELECT id FROM collections WHERE {whereClause});

            DELETE FROM collection_relationships
            WHERE collection_id IN (SELECT id FROM collections WHERE {whereClause});

            DELETE FROM collections
            WHERE {whereClause};
            """;
        cmd.ExecuteNonQuery();
        tx.Commit();

        return Task.CompletedTask;
    }

    private static string Quote(string value) => $"'{value.Replace("'", "''")}'";
}
