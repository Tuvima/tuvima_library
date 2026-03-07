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
}
