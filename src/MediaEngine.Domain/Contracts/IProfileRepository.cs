using MediaEngine.Domain.Aggregates;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Persistence contract for <see cref="Profile"/> records.
///
/// Implementations must ensure:
/// <list type="bullet">
///   <item>The seed profile (<see cref="Profile.SeedProfileId"/>) cannot be deleted.</item>
///   <item>At least one <see cref="Enums.ProfileRole.Administrator"/> profile always exists.</item>
/// </list>
/// </summary>
public interface IProfileRepository
{
    /// <summary>Returns all profiles, ordered by <c>created_at</c> ascending.</summary>
    Task<IReadOnlyList<Profile>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Returns the profile with the given <paramref name="id"/>, or <see langword="null"/>.</summary>
    Task<Profile?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Inserts a new profile.  Throws on duplicate <c>id</c>.</summary>
    Task InsertAsync(Profile profile, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing profile's display name, avatar colour, role, and PIN hash.
    /// Returns <see langword="true"/> if a row was affected.
    /// </summary>
    Task<bool> UpdateAsync(Profile profile, CancellationToken ct = default);

    /// <summary>
    /// Deletes the profile with the given <paramref name="id"/>.
    /// Returns <see langword="true"/> if a row was removed.
    /// The seed profile cannot be deleted; the implementation should return <see langword="false"/>.
    /// </summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
