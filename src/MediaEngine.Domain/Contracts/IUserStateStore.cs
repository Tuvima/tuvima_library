using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Defines the contract for persisting user-specific interaction data.
/// Spec: Phase 2 – Interfaces § IUserStateStore.
///
/// Implementations live in the Infrastructure / Storage layer, not here.
/// The Domain Core references only this interface, keeping it storage-independent.
/// </summary>
public interface IUserStateStore
{
    /// <summary>
    /// Retrieves the state for a (user, asset) pair.
    /// Returns <see langword="null"/> when no record exists yet.
    /// </summary>
    Task<UserState?> GetAsync(Guid userId, Guid assetId, CancellationToken ct = default);

    /// <summary>
    /// Persists a <see cref="UserState"/>, inserting or replacing the existing row.
    /// </summary>
    Task SaveAsync(UserState state, CancellationToken ct = default);

    /// <summary>
    /// Finds all UserState records whose underlying asset matches
    /// <paramref name="contentHash"/>.
    /// Used to re-link states after a file has been moved (Hash Dominance invariant).
    /// </summary>
    Task<IReadOnlyList<UserState>> FindByContentHashAsync(
        string contentHash, CancellationToken ct = default);

    /// <summary>
    /// Returns all states for a given user, ordered by most recently accessed.
    /// </summary>
    Task<IReadOnlyList<UserState>> GetRecentAsync(
        Guid userId, int limit = 50, CancellationToken ct = default);
}
