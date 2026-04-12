using Dapper;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

public sealed class CollectionPlacementRepository : ICollectionPlacementRepository
{
    private readonly IDatabaseConnection _db;

    public CollectionPlacementRepository(IDatabaseConnection db) => _db = db;

    public async Task<IReadOnlyList<CollectionPlacement>> GetByCollectionIdAsync(Guid collectionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var results = await conn.QueryAsync<CollectionPlacement>(
            "SELECT id AS Id, collection_id AS CollectionId, location AS Location, position AS Position, " +
            "display_limit AS DisplayLimit, display_mode AS DisplayMode, is_visible AS IsVisible, " +
            "created_at AS CreatedAt FROM collection_placements WHERE collection_id = @CollectionId ORDER BY position",
            new { CollectionId = collectionId.ToString() });
        return results.ToList();
    }

    public async Task<IReadOnlyList<CollectionPlacement>> GetByLocationAsync(string location, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var results = await conn.QueryAsync<CollectionPlacement>(
            "SELECT id AS Id, collection_id AS CollectionId, location AS Location, position AS Position, " +
            "display_limit AS DisplayLimit, display_mode AS DisplayMode, is_visible AS IsVisible, " +
            "created_at AS CreatedAt FROM collection_placements WHERE location = @Location AND is_visible = 1 ORDER BY position",
            new { Location = location });
        return results.ToList();
    }

    public async Task UpsertAsync(CollectionPlacement placement, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync("""
            INSERT INTO collection_placements (id, collection_id, location, position, display_limit, display_mode, is_visible, created_at)
            VALUES (@Id, @CollectionId, @Location, @Position, @DisplayLimit, @DisplayMode, @IsVisible, @CreatedAt)
            ON CONFLICT(id) DO UPDATE SET
                location = excluded.location,
                position = excluded.position,
                display_limit = excluded.display_limit,
                display_mode = excluded.display_mode,
                is_visible = excluded.is_visible
            """,
            new
            {
                Id = placement.Id.ToString(),
                CollectionId = placement.CollectionId.ToString(),
                placement.Location,
                placement.Position,
                placement.DisplayLimit,
                placement.DisplayMode,
                IsVisible = placement.IsVisible ? 1 : 0,
                CreatedAt = placement.CreatedAt.ToString("o"),
            });
    }

    public async Task DeleteAsync(Guid placementId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync("DELETE FROM collection_placements WHERE id = @Id",
            new { Id = placementId.ToString() });
    }

    public async Task DeleteByCollectionIdAsync(Guid collectionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync("DELETE FROM collection_placements WHERE collection_id = @CollectionId",
            new { CollectionId = collectionId.ToString() });
    }
}
