using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Identity.Contracts;

namespace MediaEngine.Identity;

/// <summary>
/// Lightweight profile management service.
///
/// Delegates to <see cref="IProfileRepository"/> for persistence.
/// Profile roles are retained as presentation data during the access-model
/// cutover. Account state and profile grants authorize all privileged work.
///
/// Spec: Settings & Management Layer — Identity & Multi-User.
/// </summary>
public sealed class ProfileService : IProfileService
{
    private readonly IProfileRepository _repo;

    public ProfileService(IProfileRepository repo)
    {
        ArgumentNullException.ThrowIfNull(repo);
        _repo = repo;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<Profile>> GetAllProfilesAsync(CancellationToken ct = default)
        => _repo.GetAllAsync(ct);

    /// <inheritdoc/>
    public Task<Profile?> GetProfileAsync(Guid id, CancellationToken ct = default)
        => _repo.GetByIdAsync(id, ct);

    /// <inheritdoc/>
    public async Task<bool> UpdateProfileAsync(Profile profile, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.DisplayName);
        if (profile.DisplayName.Length > 50)
        {
            throw new ArgumentException("Display name must be 50 characters or fewer.");
        }

        var current = await _repo.GetByIdAsync(profile.Id, ct);
        if (current is null)
        {
            return false;
        }

        profile.Role = current.Role;
        return await _repo.UpdateAsync(profile, ct);
    }
}
