using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Entities;

namespace MediaEngine.Storage.Contracts;

/// <summary>
/// Persistence contract for loading <see cref="Collection"/> aggregates with their
/// child Works, Relationships, and associated CanonicalValues.
/// </summary>
public interface ICollectionRepository
{
    /// <summary>
    /// Returns all collections, each populated with their Works, Relationships,
    /// and each Work's CanonicalValues. Editions and MediaAssets are NOT loaded.
    /// </summary>
    Task<IReadOnlyList<Collection>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Finds a collection by its display name (case-insensitive).
    /// Returns null when no collection with that name exists.
    /// Used by Great Inhale to avoid creating duplicate collections.
    /// </summary>
    Task<Collection?> FindByDisplayNameAsync(string displayName, CancellationToken ct = default);

    /// <summary>
    /// Finds a collection that has a <see cref="CollectionRelationship"/> matching the given
    /// relationship type and Wikidata QID. Returns the Collection with relationships loaded,
    /// or null if no match exists.
    /// </summary>
    Task<Collection?> FindByRelationshipQidAsync(string relType, string qid, CancellationToken ct = default);

    /// <summary>
    /// Inserts a new Collection (identified by <see cref="Collection.Id"/>) if it does not
    /// yet exist, or updates <see cref="Collection.DisplayName"/> on an existing one.
    /// Returns the collection's <see cref="Collection.Id"/>.
    /// </summary>
    Task<Guid> UpsertAsync(Collection collection, CancellationToken ct = default);

    /// <summary>
    /// Bulk-inserts collection relationship rows. Idempotent — duplicates are ignored.
    /// </summary>
    Task InsertRelationshipsAsync(IReadOnlyList<CollectionRelationship> relationships, CancellationToken ct = default);

    /// <summary>
    /// Resolves the Work ID for a given MediaAsset ID by joining through the
    /// <c>editions</c> table. Returns null when the asset is not found.
    /// </summary>
    Task<Guid?> GetWorkIdByMediaAssetAsync(Guid mediaAssetId, CancellationToken ct = default);

    /// <summary>
    /// Resolves the Work lineage for a given MediaAsset ID, from the leaf Work
    /// up through any parent and grandparent Work rows. Returns an empty list
    /// when the asset is not found.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetWorkLineageIdsByMediaAssetAsync(Guid mediaAssetId, CancellationToken ct = default);

    /// <summary>
    /// Returns the <see cref="Collection.DisplayName"/> of the Collection that owns the given
    /// Work, using a single SQL JOIN across works → collections. Returns null when the
    /// Work has no Collection assignment or when the Collection has no display name.
    /// </summary>
    Task<string?> FindCollectionNameByWorkIdAsync(Guid workId, CancellationToken ct = default);

    /// <summary>
    /// Updates a Work's <c>collection_id</c> to assign it to a Collection.
    /// </summary>
    Task AssignWorkToCollectionAsync(Guid workId, Guid collectionId, CancellationToken ct = default);

    /// <summary>
    /// Merges <paramref name="mergeCollectionId"/> into <paramref name="keepCollectionId"/>:
    /// re-assigns all Works, moves all relationships, then deletes the merged Collection.
    /// </summary>
    Task MergeCollectionsAsync(Guid keepCollectionId, Guid mergeCollectionId, CancellationToken ct = default);

    /// <summary>
    /// Sets <c>universe_mismatch = 1</c> and <c>universe_mismatch_at</c> on a Work
    /// identified by <paramref name="workId"/>. Used when the user explicitly skips
    /// Universe (Wikidata) matching for a Work via the Needs Review tab.
    /// </summary>
    Task SetUniverseMismatchAsync(Guid workId, CancellationToken ct = default);

    /// <summary>
    /// Updates a Work's <c>wikidata_status</c> and stamps <c>wikidata_checked_at</c>.
    /// Used after Wikidata QID resolution to mark a Work as "confirmed" or leave it
    /// as "pending" with a timestamp.
    /// </summary>
    Task UpdateWorkWikidataStatusAsync(Guid workId, string status, CancellationToken ct = default);

    /// <summary>
    /// Removes orphaned hierarchy records that no longer have children:
    /// <list type="number">
    ///   <item>Editions with zero MediaAssets.</item>
    ///   <item>Works with zero Editions (after the Edition pass).</item>
    ///   <item>Collections with zero Works (after the Work pass), including their
    ///     <c>collection_relationships</c> rows.</item>
    /// </list>
    /// Returns the total number of rows deleted across all three passes.
    /// Call this after bulk-deleting MediaAssets to keep the hierarchy clean.
    /// </summary>
    Task<int> PruneOrphanedHierarchyAsync(CancellationToken ct = default);

    /// <summary>Returns all child Collections of the given parent.</summary>
    Task<IReadOnlyList<Collection>> GetChildCollectionsAsync(Guid parentCollectionId, CancellationToken ct = default);

    /// <summary>Sets or clears the parent Collection for a given Collection.</summary>
    Task SetParentCollectionAsync(Guid collectionId, Guid? parentCollectionId, CancellationToken ct = default);

    /// <summary>Finds a Parent Collection by franchise/universe QID from collection_relationships.</summary>
    Task<Collection?> FindParentCollectionByRelationshipAsync(string qid, CancellationToken ct = default);

    /// <summary>
    /// Returns the IDs of all Collections that have a <c>collection_relationships</c> row with the given
    /// QID and a rel_type in ("franchise", "fictional_universe").
    /// Used by <c>ParentCollectionResolver</c> to find sibling Collections that share a franchise.
    /// </summary>
    Task<IReadOnlyList<Guid>> FindCollectionIdsByFranchiseQidAsync(string qid, CancellationToken ct = default);

    /// <summary>
    /// Returns the relationships for a single Collection without loading Works or CanonicalValues.
    /// Used by <c>ParentCollectionResolver</c> to inspect franchise signals on a specific Collection.
    /// </summary>
    Task<IReadOnlyList<CollectionRelationship>> GetRelationshipsAsync(Guid collectionId, CancellationToken ct = default);

    /// <summary>
    /// Returns the <see cref="Collection"/> row for a single Collection by its ID (no Works or relationships loaded).
    /// Returns null when the Collection does not exist.
    /// </summary>
    Task<Collection?> GetByIdAsync(Guid collectionId, CancellationToken ct = default);

    /// <summary>
    /// Finds a Collection by its Wikidata QID. Returns null if no Collection has this QID.
    /// </summary>
    Task<Collection?> FindByQidAsync(string qid, CancellationToken ct = default);

    /// <summary>Finds an Edition by its Wikidata QID. Returns null when no match exists.</summary>
    Task<Edition?> FindEditionByQidAsync(string wikidataQid, CancellationToken ct = default);

    /// <summary>Creates a new Edition under the given Work and returns the persisted entity.</summary>
    Task<Edition> CreateEditionAsync(Guid workId, string? formatLabel, string? wikidataQid, CancellationToken ct = default);

    /// <summary>Updates the <c>match_level</c> on a Work record.</summary>
    Task UpdateMatchLevelAsync(Guid workId, string matchLevel, CancellationToken ct = default);

    // ── Managed Collection methods (Smart, System, Mix, Playlist) ──────────────

    /// <summary>Returns all collections of a given type (e.g. "Smart", "System", "Mix", "Playlist").</summary>
    Task<IReadOnlyList<Collection>> GetByTypeAsync(string collectionType, CancellationToken ct = default);

    /// <summary>Returns all non-Universe collections for managed collection surfaces.</summary>
    Task<IReadOnlyList<Collection>> GetManagedCollectionsAsync(CancellationToken ct = default);

    /// <summary>Returns count of collections grouped by CollectionType for stats bar.</summary>
    Task<Dictionary<string, int>> GetCountsByTypeAsync(CancellationToken ct = default);

    /// <summary>Returns curated items for a collection (System Lists, Playlists, Mixes).</summary>
    Task<IReadOnlyList<CollectionItem>> GetCollectionItemsAsync(Guid collectionId, int limit = 20, CancellationToken ct = default);

    /// <summary>Returns total curated item count for a collection.</summary>
    Task<int> GetCollectionItemCountAsync(Guid collectionId, CancellationToken ct = default);

    /// <summary>Returns curated item counts for multiple collections in one read.</summary>
    async Task<Dictionary<Guid, int>> GetCollectionItemCountsAsync(IEnumerable<Guid> collectionIds, CancellationToken ct = default)
    {
        var result = new Dictionary<Guid, int>();
        foreach (var collectionId in collectionIds.Distinct())
            result[collectionId] = await GetCollectionItemCountAsync(collectionId, ct);
        return result;
    }

    /// <summary>Toggles the is_enabled flag on a collection.</summary>
    Task UpdateCollectionEnabledAsync(Guid collectionId, bool enabled, CancellationToken ct = default);

    /// <summary>Toggles the is_featured flag on a collection.</summary>
    Task UpdateCollectionFeaturedAsync(Guid collectionId, bool featured, CancellationToken ct = default);

    /// <summary>Sets or clears custom square artwork metadata for a collection.</summary>
    Task UpdateCollectionSquareArtworkAsync(Guid collectionId, string? localPath, string? mimeType, CancellationToken ct = default);

    /// <summary>Adds a work to a collection's curated items.</summary>
    Task AddCollectionItemAsync(CollectionItem item, CancellationToken ct = default);

    /// <summary>Removes a curated item from a collection.</summary>
    Task RemoveCollectionItemAsync(Guid itemId, CancellationToken ct = default);

    /// <summary>Persists the display order for curated collection items.</summary>
    Task ReorderCollectionItemsAsync(Guid collectionId, IReadOnlyList<Guid> itemIds, CancellationToken ct = default);

    /// <summary>
    /// Returns Universe-type collections that have at least one child Work assigned.
    /// These are Content Groups: albums, TV series, book series, movie series.
    /// Each collection is returned with its Works and each Work's CanonicalValues loaded.
    /// </summary>
    Task<IReadOnlyList<Collection>> GetContentGroupsAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns a single collection by ID with all child Works and their CanonicalValues loaded.
    /// Returns null when no collection with this ID exists.
    /// </summary>
    Task<Collection?> GetCollectionWithWorksAsync(Guid collectionId, CancellationToken ct = default);

    /// <summary>
    /// Returns the collection_id for a given work ID.
    /// Returns null when the work has no collection assignment or does not exist.
    /// Used by the Universe Enrichment service for collection-level group refresh.
    /// </summary>
    Task<Guid?> GetCollectionIdByWorkIdAsync(Guid workId, CancellationToken ct = default);

    /// <summary>Finds a collection by its rule hash for deduplication.</summary>
    Task<Collection?> FindByRuleHashAsync(string ruleHash, CancellationToken ct = default);

    /// <summary>Returns all enabled collections for placement resolution.</summary>
    Task<IReadOnlyList<Collection>> GetAllCollectionsForLocationAsync(CancellationToken ct = default);
}
