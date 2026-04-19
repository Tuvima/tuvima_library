using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IEntityAssetRepository"/>.
/// Uses Dapper for type-safe column-to-property mapping.
///
/// Manages typed image assets (Cover Art, Headshot, Banner, Square Art, Logo, Background)
/// for any entity — Work, Person, Universe, or FictionalEntity.
/// </summary>
public sealed class EntityAssetRepository : IEntityAssetRepository
{
    private readonly IDatabaseConnection _db;

    // Reusable SELECT list with aliases matching EntityAsset property names.
    private const string SelectColumns = """
        id               AS Id,
        entity_id        AS EntityId,
        entity_type      AS EntityType,
        asset_type       AS AssetTypeValue,
        image_url        AS ImageUrl,
        local_image_path AS LocalImagePath,
        source_provider  AS SourceProvider,
        is_preferred     AS IsPreferred,
        is_user_override AS IsUserOverride,
        created_at       AS CreatedAt,
        updated_at       AS UpdatedAt
        """;

    public EntityAssetRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<EntityAsset>> GetByEntityAsync(
        string entityId, string? assetType = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);

        using var conn = _db.CreateConnection();

        IEnumerable<EntityAsset> rows;

        if (assetType is null)
        {
            rows = conn.Query<EntityAsset>($"""
                SELECT {SelectColumns}
                FROM   entity_assets
                WHERE  entity_id = @entityId
                ORDER BY asset_type, is_preferred DESC, created_at;
                """, new { entityId });
        }
        else
        {
            rows = conn.Query<EntityAsset>($"""
                SELECT {SelectColumns}
                FROM   entity_assets
                WHERE  entity_id = @entityId
                AND    asset_type = @assetType
                ORDER BY is_preferred DESC, created_at;
                """, new { entityId, assetType });
        }

        return Task.FromResult<IReadOnlyList<EntityAsset>>(rows.ToList());
    }

    /// <inheritdoc/>
    public Task<EntityAsset?> FindByIdAsync(Guid assetId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var result = conn.QuerySingleOrDefault<EntityAsset>($"""
            SELECT {SelectColumns}
            FROM   entity_assets
            WHERE  id = @assetId
            LIMIT  1;
            """, new { assetId = assetId.ToString() });

        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task UpsertAsync(EntityAsset asset, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(asset);

        using var conn = _db.CreateConnection();
        conn.Execute("""
            INSERT INTO entity_assets
                (id, entity_id, entity_type, asset_type, image_url,
                 local_image_path, source_provider, is_preferred, is_user_override, created_at)
            VALUES
                (@Id, @EntityId, @EntityType, @AssetTypeValue, @ImageUrl,
                 @LocalImagePath, @SourceProvider, @IsPreferred, @IsUserOverride, @CreatedAt)
            ON CONFLICT(id) DO UPDATE SET
                image_url        = excluded.image_url,
                local_image_path = excluded.local_image_path,
                is_preferred     = excluded.is_preferred,
                is_user_override = excluded.is_user_override,
                updated_at       = datetime('now');
            """,
            new
            {
                Id = asset.Id.ToString(),
                asset.EntityId,
                asset.EntityType,
                asset.AssetTypeValue,
                asset.ImageUrl,
                asset.LocalImagePath,
                asset.SourceProvider,
                IsPreferred = asset.IsPreferred ? 1 : 0,
                IsUserOverride = asset.IsUserOverride ? 1 : 0,
                CreatedAt = asset.CreatedAt.ToString("O"),
            });

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task SetPreferredAsync(Guid assetId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        using var tx = conn.BeginTransaction();

        // Find the target asset's entity_id and asset_type.
        var target = conn.QuerySingleOrDefault<(string EntityId, string AssetType)>("""
            SELECT entity_id AS EntityId, asset_type AS AssetType
            FROM   entity_assets
            WHERE  id = @assetId;
            """, new { assetId = assetId.ToString() }, tx);

        if (target == default)
        {
            tx.Commit();
            return Task.CompletedTask;
        }

        // Clear preferred flag on all assets with the same entity + asset type.
        conn.Execute("""
            UPDATE entity_assets
            SET    is_preferred = 0,
                   updated_at  = datetime('now')
            WHERE  entity_id  = @entityId
            AND    asset_type = @assetType
            AND    is_preferred = 1;
            """, new { entityId = target.EntityId, assetType = target.AssetType }, tx);

        // Set the target as preferred.
        conn.Execute("""
            UPDATE entity_assets
            SET    is_preferred = 1,
                   updated_at  = datetime('now')
            WHERE  id = @assetId;
            """, new { assetId = assetId.ToString() }, tx);

        tx.Commit();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteByEntityAsync(string entityId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);

        using var conn = _db.CreateConnection();
        conn.Execute("""
            DELETE FROM entity_assets
            WHERE  entity_id = @entityId;
            """, new { entityId });

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteAsync(Guid assetId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        conn.Execute("""
            DELETE FROM entity_assets
            WHERE  id = @assetId;
            """, new { assetId = assetId.ToString() });

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<EntityAsset?> GetPreferredAsync(
        string entityId, string assetType, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetType);

        using var conn = _db.CreateConnection();
        var result = conn.QuerySingleOrDefault<EntityAsset>($"""
            SELECT {SelectColumns}
            FROM   entity_assets
            WHERE  entity_id   = @entityId
            AND    asset_type  = @assetType
            AND    is_preferred = 1
            LIMIT  1;
            """, new { entityId, assetType });

        return Task.FromResult(result);
    }
}
