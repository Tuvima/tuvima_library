using MediaEngine.Domain.Aggregates;

namespace MediaEngine.Identity.Contracts;

/// <summary>
/// Service contract for local user profile management.
///
/// Profiles hold display and experience preferences. Account/profile grants
/// are the authority for access and administrator capabilities.
///
/// Spec: Settings & Management Layer — Identity & Multi-User.
/// </summary>
public interface IProfileService
{
    /// <summary>Returns all profiles, ordered by creation date ascending.</summary>
    Task<IReadOnlyList<Profile>> GetAllProfilesAsync(CancellationToken ct = default);

    /// <summary>Returns the profile with the given <paramref name="id"/>, or <see langword="null"/>.</summary>
    Task<Profile?> GetProfileAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing profile's display and experience preferences.
    /// The stored presentation role is preserved and does not authorize the caller.
    /// </summary>
    Task<bool> UpdateProfileAsync(Profile profile, CancellationToken ct = default);
}
