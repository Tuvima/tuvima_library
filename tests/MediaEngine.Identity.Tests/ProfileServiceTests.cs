using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Identity.Tests;

public sealed class ProfileServiceTests
{
    [Theory]
    [InlineData(ProfileRole.Administrator, ProfileRole.RestrictedProfile)]
    [InlineData(ProfileRole.RestrictedProfile, ProfileRole.Administrator)]
    public async Task UpdateProfileAsync_PersistsExperienceAndPreservesPresentationRole(
        ProfileRole storedRole,
        ProfileRole requestedRole)
    {
        var stored = NewProfile(storedRole);
        var repository = new FakeProfileRepository(stored);
        var service = new ProfileService(repository);
        var update = Clone(stored);
        update.DisplayName = "Updated display";
        update.AvatarColor = "#123456";
        update.NavigationConfig = "{\"lane\":\"listen\"}";
        update.Role = requestedRole;

        Assert.True(await service.UpdateProfileAsync(update));

        var persisted = Assert.IsType<Profile>(await repository.GetByIdAsync(stored.Id));
        Assert.Equal("Updated display", persisted.DisplayName);
        Assert.Equal("#123456", persisted.AvatarColor);
        Assert.Equal("{\"lane\":\"listen\"}", persisted.NavigationConfig);
        Assert.Equal(storedRole, persisted.Role);
    }

    [Fact]
    public async Task UpdateProfileAsync_ReturnsFalseForMissingProfile()
    {
        var repository = new FakeProfileRepository();
        var service = new ProfileService(repository);

        Assert.False(await service.UpdateProfileAsync(NewProfile(ProfileRole.Administrator)));
        Assert.Equal(0, repository.UpdateCount);
    }

    private static Profile NewProfile(ProfileRole role) => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = $"{role} Profile",
        AvatarColor = "#7C4DFF",
        Role = role,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static Profile Clone(Profile profile) => new()
    {
        Id = profile.Id,
        DisplayName = profile.DisplayName,
        AvatarColor = profile.AvatarColor,
        AvatarImagePath = profile.AvatarImagePath,
        Role = profile.Role,
        CreatedAt = profile.CreatedAt,
        NavigationConfig = profile.NavigationConfig,
    };

    private sealed class FakeProfileRepository(params Profile[] profiles) : IProfileRepository
    {
        private readonly List<Profile> _profiles = profiles.Select(Clone).ToList();
        public int UpdateCount { get; private set; }

        public Task<IReadOnlyList<Profile>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Profile>>(_profiles.Select(Clone).ToList());

        public Task<Profile?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_profiles.Where(profile => profile.Id == id).Select(Clone).SingleOrDefault());

        public Task<bool> UpdateAsync(Profile profile, CancellationToken ct = default)
        {
            var index = _profiles.FindIndex(candidate => candidate.Id == profile.Id);
            if (index < 0)
            {
                return Task.FromResult(false);
            }

            _profiles[index] = Clone(profile);
            UpdateCount++;
            return Task.FromResult(true);
        }
    }
}
