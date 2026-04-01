using Dapper;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

public sealed class HubPlacementRepository : IHubPlacementRepository
{
    private readonly IDatabaseConnection _db;

    public HubPlacementRepository(IDatabaseConnection db) => _db = db;

    public async Task<IReadOnlyList<HubPlacement>> GetByHubIdAsync(Guid hubId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var results = await conn.QueryAsync<HubPlacement>(
            "SELECT id AS Id, hub_id AS HubId, location AS Location, position AS Position, " +
            "display_limit AS DisplayLimit, display_mode AS DisplayMode, is_visible AS IsVisible, " +
            "created_at AS CreatedAt FROM hub_placements WHERE hub_id = @HubId ORDER BY position",
            new { HubId = hubId.ToString() });
        return results.ToList();
    }

    public async Task<IReadOnlyList<HubPlacement>> GetByLocationAsync(string location, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var results = await conn.QueryAsync<HubPlacement>(
            "SELECT id AS Id, hub_id AS HubId, location AS Location, position AS Position, " +
            "display_limit AS DisplayLimit, display_mode AS DisplayMode, is_visible AS IsVisible, " +
            "created_at AS CreatedAt FROM hub_placements WHERE location = @Location AND is_visible = 1 ORDER BY position",
            new { Location = location });
        return results.ToList();
    }

    public async Task UpsertAsync(HubPlacement placement, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync("""
            INSERT INTO hub_placements (id, hub_id, location, position, display_limit, display_mode, is_visible, created_at)
            VALUES (@Id, @HubId, @Location, @Position, @DisplayLimit, @DisplayMode, @IsVisible, @CreatedAt)
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
                HubId = placement.HubId.ToString(),
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
        await conn.ExecuteAsync("DELETE FROM hub_placements WHERE id = @Id",
            new { Id = placementId.ToString() });
    }

    public async Task DeleteByHubIdAsync(Guid hubId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync("DELETE FROM hub_placements WHERE hub_id = @HubId",
            new { HubId = hubId.ToString() });
    }
}
