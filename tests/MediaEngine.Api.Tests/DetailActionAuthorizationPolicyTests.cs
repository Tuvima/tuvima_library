using MediaEngine.Api.Services.Details.Internals;
using MediaEngine.Domain;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Api.Tests;

public sealed class DetailActionAuthorizationPolicyTests
{
    [Theory]
    [InlineData(AppRoles.Administrator)]
    [InlineData(AppRoles.StandardUser)]
    public async Task ResolveAsync_AllowsManagersWithoutAProfileContext(string callerRole)
    {
        var result = await DetailActionAuthorizationPolicy.ResolveAsync(callerRole, null, null, CancellationToken.None);

        Assert.True(result.Allows("edit"));
    }

    [Theory]
    [InlineData(AppRoles.RestrictedProfile)]
    [InlineData(null)]
    [InlineData("unknown")]
    public async Task ResolveAsync_DeniesCallersWithoutManagementRights(string? callerRole)
    {
        var result = await DetailActionAuthorizationPolicy.ResolveAsync(callerRole, null, null, CancellationToken.None);

        Assert.False(result.Allows("edit"));
    }

    [Fact]
    public async Task ResolveAsync_RequiresBothCallerAndSelectedProfileToManageMetadata()
    {
        var consumer = ProfileWithRole(ProfileRole.RestrictedProfile);
        var curator = ProfileWithRole(ProfileRole.StandardUser);
        var profiles = new FakeProfileRepository(consumer, curator);

        var consumerResult = await DetailActionAuthorizationPolicy.ResolveAsync(
            AppRoles.Administrator,
            consumer.Id,
            profiles,
            CancellationToken.None);
        var curatorResult = await DetailActionAuthorizationPolicy.ResolveAsync(
            AppRoles.Administrator,
            curator.Id,
            profiles,
            CancellationToken.None);
        var missingResult = await DetailActionAuthorizationPolicy.ResolveAsync(
            AppRoles.Administrator,
            Guid.NewGuid(),
            profiles,
            CancellationToken.None);

        Assert.False(consumerResult.Allows("edit"));
        Assert.True(curatorResult.Allows("edit"));
        Assert.False(missingResult.Allows("edit"));
    }

    [Fact]
    public async Task ResolveAsync_FailsClosedForUnknownActions()
    {
        var result = await DetailActionAuthorizationPolicy.ResolveAsync(
            AppRoles.Administrator,
            null,
            null,
            CancellationToken.None);

        Assert.False(result.Allows("delete-library-item"));
    }

    private static Profile ProfileWithRole(ProfileRole role) => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = role.ToString(),
        Role = role,
    };

    private sealed class FakeProfileRepository(params Profile[] profiles) : IProfileRepository
    {
        private readonly IReadOnlyDictionary<Guid, Profile> _profiles = profiles.ToDictionary(profile => profile.Id);

        public Task<IReadOnlyList<Profile>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Profile>>(_profiles.Values.ToList());

        public Task<Profile?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_profiles.GetValueOrDefault(id));

        public Task InsertAsync(Profile profile, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> UpdateAsync(Profile profile, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
