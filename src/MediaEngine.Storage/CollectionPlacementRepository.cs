using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

public sealed class CollectionPlacementRepository : ICollectionPlacementRepository
{
    private readonly IDatabaseConnection _db;

    public CollectionPlacementRepository(IDatabaseConnection db) => _db = db;

    public Task<IReadOnlyList<CollectionPlacement>> GetByCollectionIdAsync(Guid collectionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var results = conn.Query<CollectionPlacement>(
            "SELECT id AS Id, collection_id AS CollectionId, location AS Location, position AS Position, " +
            "display_limit AS DisplayLimit, display_mode AS DisplayMode, is_visible AS IsVisible, " +
            "created_at AS CreatedAt FROM collection_placements WHERE collection_id = @CollectionId ORDER BY position",
            new { CollectionId = collectionId });
        return Task.FromResult<IReadOnlyList<CollectionPlacement>>(results.ToList());
    }

    public Task<IReadOnlyList<CollectionPlacement>> GetByLocationAsync(string location, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var results = conn.Query<CollectionPlacement>(
            "SELECT id AS Id, collection_id AS CollectionId, location AS Location, position AS Position, " +
            "display_limit AS DisplayLimit, display_mode AS DisplayMode, is_visible AS IsVisible, " +
            "created_at AS CreatedAt FROM collection_placements WHERE location = @Location AND is_visible = 1 ORDER BY position",
            new { Location = location });
        return Task.FromResult<IReadOnlyList<CollectionPlacement>>(results.ToList());
    }

    public Task UpsertAsync(CollectionPlacement placement, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        conn.Execute("""
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
                placement.Id,
                placement.CollectionId,
                placement.Location,
                placement.Position,
                placement.DisplayLimit,
                placement.DisplayMode,
                IsVisible = placement.IsVisible ? 1 : 0,
                CreatedAt = placement.CreatedAt.ToString("o"),
            });
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid placementId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        conn.Execute("DELETE FROM collection_placements WHERE id = @Id",
            new { Id = placementId });
        return Task.CompletedTask;
    }

    public Task DeleteByCollectionIdAsync(Guid collectionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        conn.Execute("DELETE FROM collection_placements WHERE collection_id = @CollectionId",
            new { CollectionId = collectionId });
        return Task.CompletedTask;
    }
}
