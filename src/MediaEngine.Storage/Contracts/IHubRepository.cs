using MediaEngine.Domain.Aggregates;

namespace MediaEngine.Storage.Contracts;

/// <summary>
/// Persistence contract for loading <see cref="Hub"/> aggregates with their
/// child Works and associated CanonicalValues.
/// </summary>
public interface IHubRepository
{
    /// <summary>
    /// Returns all hubs, each populated with their Works and each Work's
    /// CanonicalValues. Editions and MediaAssets are NOT loaded (not needed
    /// by the list endpoint; add a FindByIdAsync overload later if required).
    /// </summary>
    Task<IReadOnlyList<Hub>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Finds a hub by its display name (case-insensitive).
    /// Returns null when no hub with that name exists.
    /// Used by Great Inhale to avoid creating duplicate hubs.
    /// </summary>
    Task<Hub?> FindByDisplayNameAsync(string displayName, CancellationToken ct = default);

    /// <summary>
    /// Inserts a new Hub (identified by <see cref="Hub.Id"/>) if it does not
    /// yet exist, or updates <see cref="Hub.DisplayName"/> on an existing one.
    /// Returns the hub's <see cref="Hub.Id"/>.
    /// </summary>
    Task<Guid> UpsertAsync(Hub hub, CancellationToken ct = default);

    /// <summary>
    /// Sets <c>universe_mismatch = 1</c> and <c>universe_mismatch_at</c> on a Work
    /// identified by <paramref name="workId"/>. Used when the user explicitly skips
    /// Universe (Wikidata) matching for a Work via the Needs Review tab.
    /// </summary>
    Task SetUniverseMismatchAsync(Guid workId, CancellationToken ct = default);
}
