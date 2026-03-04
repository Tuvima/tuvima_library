using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Identity.Contracts;

namespace MediaEngine.Identity;

/// <summary>
/// Lightweight profile management service.
///
/// Delegates to <see cref="IProfileRepository"/> for persistence.
/// Enforces business rules:
/// <list type="bullet">
///   <item>Display names must be 1–50 characters.</item>
///   <item>The seed "Owner" profile cannot be deleted.</item>
///   <item>The last Administrator profile cannot be deleted.</item>
/// </list>
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
    public async Task<Profile> CreateProfileAsync(
        string displayName,
        ProfileRole role,
        string avatarColor,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (displayName.Length > 50)
            throw new ArgumentException("Display name must be 50 characters or fewer.", nameof(displayName));

        var profile = new Profile
        {
            Id          = Guid.NewGuid(),
            DisplayName = displayName.Trim(),
            AvatarColor = string.IsNullOrWhiteSpace(avatarColor) ? "#7C4DFF" : avatarColor.Trim(),
            Role        = role,
            CreatedAt   = DateTimeOffset.UtcNow,
        };

        await _repo.InsertAsync(profile, ct);
        return profile;
    }

    /// <inheritdoc/>
    public Task<bool> UpdateProfileAsync(Profile profile, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.DisplayName);
        if (profile.DisplayName.Length > 50)
            throw new ArgumentException("Display name must be 50 characters or fewer.");

        return _repo.UpdateAsync(profile, ct);
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteProfileAsync(Guid id, CancellationToken ct = default)
    {
        // Seed profile is always protected.
        if (id == Profile.SeedProfileId)
            return false;

        // Ensure at least one Administrator remains after deletion.
        var all = await _repo.GetAllAsync(ct);
        var target = all.FirstOrDefault(p => p.Id == id);
        if (target is null)
            return false;

        if (target.Role == ProfileRole.Administrator)
        {
            var adminCount = all.Count(p => p.Role == ProfileRole.Administrator);
            if (adminCount <= 1)
                return false; // Cannot delete the last Administrator.
        }

        return await _repo.DeleteAsync(id, ct);
    }

    /// <inheritdoc/>
    public async Task<Profile> GetDefaultProfileAsync(CancellationToken ct = default)
    {
        var profile = await _repo.GetByIdAsync(Profile.SeedProfileId, ct);
        return profile ?? new Profile
        {
            Id          = Profile.SeedProfileId,
            DisplayName = "Owner",
            AvatarColor = "#7C4DFF",
            Role        = ProfileRole.Administrator,
            CreatedAt   = DateTimeOffset.UtcNow,
        };
    }
}
