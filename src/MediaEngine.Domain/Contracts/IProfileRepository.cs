using MediaEngine.Domain.Aggregates;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Persistence contract for <see cref="Profile"/> records.
///
/// Profile lifecycle and administrator safety are owned by the account/grant
/// repository. This projection persists display and experience preferences only.
/// </summary>
public interface IProfileRepository
{
    /// <summary>Returns all profiles, ordered by <c>created_at</c> ascending.</summary>
    Task<IReadOnlyList<Profile>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Returns the profile with the given <paramref name="id"/>, or <see langword="null"/>.</summary>
    Task<Profile?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Updates display and experience preferences without changing the retained
    /// presentation role. Returns <see langword="true"/> if a row was affected.
    /// </summary>
    Task<bool> UpdateAsync(Profile profile, CancellationToken ct = default);
}
