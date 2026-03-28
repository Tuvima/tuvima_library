using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// CRUD operations for the <c>entity_assets</c> table — formalized typed
/// image storage for any entity (Work, Person, Universe, FictionalEntity).
/// </summary>
public interface IEntityAssetRepository
{
    /// <summary>Get all assets for an entity, optionally filtered by type.</summary>
    Task<IReadOnlyList<EntityAsset>> GetByEntityAsync(
        string entityId, string? assetType = null, CancellationToken ct = default);

    /// <summary>Insert or update an asset. Upsert keyed on (entity_id, entity_type, asset_type, source_provider).</summary>
    Task UpsertAsync(EntityAsset asset, CancellationToken ct = default);

    /// <summary>
    /// Set a specific asset as preferred for its entity + asset type.
    /// Clears preferred flag on other assets of the same type for the same entity.
    /// </summary>
    Task SetPreferredAsync(Guid assetId, CancellationToken ct = default);

    /// <summary>Delete all assets for an entity (used during entity cleanup).</summary>
    Task DeleteByEntityAsync(string entityId, CancellationToken ct = default);

    /// <summary>Get the preferred asset of a given type for an entity.</summary>
    Task<EntityAsset?> GetPreferredAsync(
        string entityId, string assetType, CancellationToken ct = default);
}
