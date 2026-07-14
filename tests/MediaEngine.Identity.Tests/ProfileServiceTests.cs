using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Identity.Tests;

public sealed class ProfileServiceTests
{
    [Fact]
    public async Task UpdateProfileAsync_RejectsSeedOwnerDemotion()
    {
        var repository = new FakeProfileRepository(SeedOwner());
        var service = new ProfileService(repository);
        var update = SeedOwner();
        update.Role = ProfileRole.Consumer;

        var updated = await service.UpdateProfileAsync(update);

        Assert.False(updated);
        Assert.Equal(0, repository.UpdateCount);
        Assert.Equal(ProfileRole.Administrator, repository.Profiles.Single().Role);
    }

    [Fact]
    public async Task UpdateProfileAsync_RejectsLastAdministratorDemotion()
    {
        var administrator = NewProfile(ProfileRole.Administrator);
        var repository = new FakeProfileRepository(administrator);
        var service = new ProfileService(repository);
        var update = Clone(administrator);
        update.Role = ProfileRole.Curator;

        var updated = await service.UpdateProfileAsync(update);

        Assert.False(updated);
        Assert.Equal(0, repository.UpdateCount);
        Assert.Equal(ProfileRole.Administrator, repository.Profiles.Single().Role);
    }

    [Fact]
    public async Task UpdateProfileAsync_AllowsAdministratorDemotionWhenAnotherRemains()
    {
        var first = NewProfile(ProfileRole.Administrator);
        var second = NewProfile(ProfileRole.Administrator);
        var repository = new FakeProfileRepository(first, second);
        var service = new ProfileService(repository);
        var update = Clone(first);
        update.Role = ProfileRole.Curator;

        var updated = await service.UpdateProfileAsync(update);

        Assert.True(updated);
        Assert.Equal(1, repository.UpdateCount);
        Assert.Equal(ProfileRole.Curator, repository.Profiles.Single(profile => profile.Id == first.Id).Role);
        Assert.Equal(ProfileRole.Administrator, repository.Profiles.Single(profile => profile.Id == second.Id).Role);
    }

    [Fact]
    public async Task UpdateProfileAsync_AllowsNonRoleChangesForLastAdministrator()
    {
        var administrator = NewProfile(ProfileRole.Administrator);
        var repository = new FakeProfileRepository(administrator);
        var service = new ProfileService(repository);
        var update = Clone(administrator);
        update.DisplayName = "Updated Owner";

        var updated = await service.UpdateProfileAsync(update);

        Assert.True(updated);
        Assert.Equal(1, repository.UpdateCount);
        Assert.Equal("Updated Owner", repository.Profiles.Single().DisplayName);
    }

    private static Profile SeedOwner() => new()
    {
        Id = Profile.SeedProfileId,
        DisplayName = "Owner",
        AvatarColor = "#7C4DFF",
        Role = ProfileRole.Administrator,
        CreatedAt = DateTimeOffset.UtcNow,
    };

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
        PinHash = profile.PinHash,
        CreatedAt = profile.CreatedAt,
        NavigationConfig = profile.NavigationConfig,
    };

    private sealed class FakeProfileRepository(params Profile[] profiles) : IProfileRepository
    {
        private readonly List<Profile> _profiles = profiles.Select(Clone).ToList();

        public IReadOnlyList<Profile> Profiles => _profiles;
        public int UpdateCount { get; private set; }

        public Task<IReadOnlyList<Profile>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Profile>>(_profiles.Select(Clone).ToList());

        public Task<Profile?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_profiles.Where(profile => profile.Id == id).Select(Clone).SingleOrDefault());

        public Task InsertAsync(Profile profile, CancellationToken ct = default)
        {
            _profiles.Add(Clone(profile));
            return Task.CompletedTask;
        }

        public Task<bool> UpdateAsync(Profile profile, CancellationToken ct = default)
        {
            var index = _profiles.FindIndex(candidate => candidate.Id == profile.Id);
            if (index < 0)
                return Task.FromResult(false);

            _profiles[index] = Clone(profile);
            UpdateCount++;
            return Task.FromResult(true);
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_profiles.RemoveAll(profile => profile.Id == id) > 0);
    }
}
