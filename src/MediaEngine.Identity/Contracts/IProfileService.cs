using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Identity.Contracts;

/// <summary>
/// Service contract for local user profile management.
///
/// Profiles are lightweight local identities — no OAuth, no JWT.
/// Each profile has a display name, an avatar colour, and a role
/// that determines which Settings tabs are visible.
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
    /// Creates a new profile with the given properties.
    /// Returns the created <see cref="Profile"/> with a generated <c>Id</c>.
    /// </summary>
    Task<Profile> CreateProfileAsync(
        string displayName,
        ProfileRole role,
        string avatarColor,
        CancellationToken ct = default);

    /// <summary>
    /// Updates an existing profile's display name, avatar colour, and role.
    /// Returns <see langword="true"/> if the update was applied.
    /// </summary>
    Task<bool> UpdateProfileAsync(Profile profile, CancellationToken ct = default);

    /// <summary>
    /// Deletes the profile with the given <paramref name="id"/>.
    /// Returns <see langword="false"/> if the profile is the seed "Owner" profile
    /// or if it is the last Administrator.
    /// </summary>
    Task<bool> DeleteProfileAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Returns the default seed profile (the "Owner" Administrator).
    /// This profile always exists and cannot be deleted.
    /// </summary>
    Task<Profile> GetDefaultProfileAsync(CancellationToken ct = default);
}
