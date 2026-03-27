using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Entities;

namespace MediaEngine.Storage.Contracts;

/// <summary>
/// Persistence contract for loading <see cref="Hub"/> aggregates with their
/// child Works, Relationships, and associated CanonicalValues.
/// </summary>
public interface IHubRepository
{
    /// <summary>
    /// Returns all hubs, each populated with their Works, Relationships,
    /// and each Work's CanonicalValues. Editions and MediaAssets are NOT loaded.
    /// </summary>
    Task<IReadOnlyList<Hub>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Finds a hub by its display name (case-insensitive).
    /// Returns null when no hub with that name exists.
    /// Used by Great Inhale to avoid creating duplicate hubs.
    /// </summary>
    Task<Hub?> FindByDisplayNameAsync(string displayName, CancellationToken ct = default);

    /// <summary>
    /// Finds a hub that has a <see cref="HubRelationship"/> matching the given
    /// relationship type and Wikidata QID. Returns the Hub with relationships loaded,
    /// or null if no match exists.
    /// </summary>
    Task<Hub?> FindByRelationshipQidAsync(string relType, string qid, CancellationToken ct = default);

    /// <summary>
    /// Inserts a new Hub (identified by <see cref="Hub.Id"/>) if it does not
    /// yet exist, or updates <see cref="Hub.DisplayName"/> on an existing one.
    /// Returns the hub's <see cref="Hub.Id"/>.
    /// </summary>
    Task<Guid> UpsertAsync(Hub hub, CancellationToken ct = default);

    /// <summary>
    /// Bulk-inserts hub relationship rows. Idempotent — duplicates are ignored.
    /// </summary>
    Task InsertRelationshipsAsync(IReadOnlyList<HubRelationship> relationships, CancellationToken ct = default);

    /// <summary>
    /// Resolves the Work ID for a given MediaAsset ID by joining through the
    /// <c>editions</c> table. Returns null when the asset is not found.
    /// </summary>
    Task<Guid?> GetWorkIdByMediaAssetAsync(Guid mediaAssetId, CancellationToken ct = default);

    /// <summary>
    /// Returns the <see cref="Hub.DisplayName"/> of the Hub that owns the given
    /// Work, using a single SQL JOIN across works → hubs. Returns null when the
    /// Work has no Hub assignment or when the Hub has no display name.
    /// </summary>
    Task<string?> FindHubNameByWorkIdAsync(Guid workId, CancellationToken ct = default);

    /// <summary>
    /// Updates a Work's <c>hub_id</c> to assign it to a Hub.
    /// </summary>
    Task AssignWorkToHubAsync(Guid workId, Guid hubId, CancellationToken ct = default);

    /// <summary>
    /// Merges <paramref name="mergeHubId"/> into <paramref name="keepHubId"/>:
    /// re-assigns all Works, moves all relationships, then deletes the merged Hub.
    /// </summary>
    Task MergeHubsAsync(Guid keepHubId, Guid mergeHubId, CancellationToken ct = default);

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
    ///   <item>Hubs with zero Works (after the Work pass), including their
    ///     <c>hub_relationships</c> rows.</item>
    /// </list>
    /// Returns the total number of rows deleted across all three passes.
    /// Call this after bulk-deleting MediaAssets to keep the hierarchy clean.
    /// </summary>
    Task<int> PruneOrphanedHierarchyAsync(CancellationToken ct = default);

    /// <summary>Returns all child Hubs of the given parent.</summary>
    Task<IReadOnlyList<Hub>> GetChildHubsAsync(Guid parentHubId, CancellationToken ct = default);

    /// <summary>Sets or clears the parent Hub for a given Hub.</summary>
    Task SetParentHubAsync(Guid hubId, Guid? parentHubId, CancellationToken ct = default);

    /// <summary>Finds a Parent Hub by franchise/universe QID from hub_relationships.</summary>
    Task<Hub?> FindParentHubByRelationshipAsync(string qid, CancellationToken ct = default);

    /// <summary>
    /// Returns the IDs of all Hubs that have a <c>hub_relationships</c> row with the given
    /// QID and a rel_type in ("franchise", "fictional_universe").
    /// Used by <c>ParentHubResolver</c> to find sibling Hubs that share a franchise.
    /// </summary>
    Task<IReadOnlyList<Guid>> FindHubIdsByFranchiseQidAsync(string qid, CancellationToken ct = default);

    /// <summary>
    /// Returns the relationships for a single Hub without loading Works or CanonicalValues.
    /// Used by <c>ParentHubResolver</c> to inspect franchise signals on a specific Hub.
    /// </summary>
    Task<IReadOnlyList<HubRelationship>> GetRelationshipsAsync(Guid hubId, CancellationToken ct = default);

    /// <summary>
    /// Returns the <see cref="Hub"/> row for a single Hub by its ID (no Works or relationships loaded).
    /// Returns null when the Hub does not exist.
    /// </summary>
    Task<Hub?> GetByIdAsync(Guid hubId, CancellationToken ct = default);

    /// <summary>
    /// Finds a Hub by its Wikidata QID. Returns null if no Hub has this QID.
    /// </summary>
    Task<Hub?> FindByQidAsync(string qid, CancellationToken ct = default);

    /// <summary>Finds an Edition by its Wikidata QID. Returns null when no match exists.</summary>
    Task<Edition?> FindEditionByQidAsync(string wikidataQid, CancellationToken ct = default);

    /// <summary>Creates a new Edition under the given Work and returns the persisted entity.</summary>
    Task<Edition> CreateEditionAsync(Guid workId, string? formatLabel, string? wikidataQid, CancellationToken ct = default);

    /// <summary>Updates the <c>match_level</c> on a Work record.</summary>
    Task UpdateMatchLevelAsync(Guid workId, string matchLevel, CancellationToken ct = default);

    // ── Managed Hub methods (Smart, System, Mix, Playlist) ──────────────

    /// <summary>Returns all hubs of a given type (e.g. "Smart", "System", "Mix", "Playlist").</summary>
    Task<IReadOnlyList<Hub>> GetByTypeAsync(string hubType, CancellationToken ct = default);

    /// <summary>Returns all non-Universe hubs for the Vault Hubs tab.</summary>
    Task<IReadOnlyList<Hub>> GetManagedHubsAsync(CancellationToken ct = default);

    /// <summary>Returns count of hubs grouped by HubType for stats bar.</summary>
    Task<Dictionary<string, int>> GetCountsByTypeAsync(CancellationToken ct = default);

    /// <summary>Returns curated items for a hub (System Lists, Playlists, Mixes).</summary>
    Task<IReadOnlyList<HubItem>> GetHubItemsAsync(Guid hubId, int limit = 20, CancellationToken ct = default);

    /// <summary>Returns total curated item count for a hub.</summary>
    Task<int> GetHubItemCountAsync(Guid hubId, CancellationToken ct = default);

    /// <summary>Toggles the is_enabled flag on a hub.</summary>
    Task UpdateHubEnabledAsync(Guid hubId, bool enabled, CancellationToken ct = default);

    /// <summary>Toggles the is_featured flag on a hub.</summary>
    Task UpdateHubFeaturedAsync(Guid hubId, bool featured, CancellationToken ct = default);

    /// <summary>Adds a work to a hub's curated items.</summary>
    Task AddHubItemAsync(HubItem item, CancellationToken ct = default);

    /// <summary>Removes a curated item from a hub.</summary>
    Task RemoveHubItemAsync(Guid itemId, CancellationToken ct = default);
}
