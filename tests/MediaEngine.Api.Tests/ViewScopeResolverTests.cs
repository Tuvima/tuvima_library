using MediaEngine.Api.Services.View;
using MediaEngine.Domain.PersonalMedia;

namespace MediaEngine.Api.Tests;

public sealed class ViewScopeResolverTests
{
    [Fact]
    public async Task SharedLibraryAccessDoesNotExposeOtherProfileScopes()
    {
        var caller = State(access: true, include: false);
        var included = State(access: false, include: true);
        var excluded = State(access: true, include: false);
        var resolver = new ViewScopeResolver(new ScopeStore(caller, included, excluded));

        var resolution = Assert.IsType<ViewScopeResolution>(await resolver.ResolveAsync(
            new ViewRequestProfile(caller.Policy.ProfileId, "RestrictedProfile"),
            ViewScopeRequest.Shared));

        Assert.Equal(ViewScopeKind.Shared, resolution.Scope.Kind);
        Assert.Empty(resolution.Scope.LibraryIds);
        Assert.DoesNotContain(resolution.AvailableScopes, option => option.Kind == ViewScopeKind.Profile);
    }

    [Fact]
    public async Task SubmissionPermissionDoesNotGrantCallerSharedAccess()
    {
        var caller = State(access: false, include: true);
        var other = State(access: false, include: true);
        var resolver = new ViewScopeResolver(new ScopeStore(caller, other));

        var resolution = Assert.IsType<ViewScopeResolution>(await resolver.ResolveAsync(
            new ViewRequestProfile(caller.Policy.ProfileId, "RestrictedProfile"),
            ViewScopeRequest.Shared));

        Assert.Equal(ViewScopeKind.Mine, resolution.Scope.Kind);
        Assert.True(resolution.Scope.WasFallback);
        Assert.Equal([caller.PersonalSpace!.LibraryId], resolution.Scope.LibraryIds);
        Assert.Single(resolution.AvailableScopes);
    }

    [Fact]
    public async Task OtherProfileScopeFallsBackToMineWithoutEnumeratingIt()
    {
        var caller = State(access: true, include: true);
        var revoked = State(access: false, include: false);
        var resolver = new ViewScopeResolver(new ScopeStore(caller, revoked));

        var resolution = Assert.IsType<ViewScopeResolution>(await resolver.ResolveAsync(
            new ViewRequestProfile(caller.Policy.ProfileId, "RestrictedProfile"),
            ViewScopeRequest.ForProfile(revoked.Policy.ProfileId)));

        Assert.Equal(ViewScopeKind.Mine, resolution.Scope.Kind);
        Assert.True(resolution.Scope.WasFallback);
        Assert.DoesNotContain(resolution.AvailableScopes, option => option.ProfileId == revoked.Policy.ProfileId);
    }

    [Fact]
    public async Task DisabledCallerHasNoViewScope()
    {
        var caller = State(access: true, include: true);
        caller = caller with { Policy = caller.Policy with { ViewEnabled = false } };
        var resolver = new ViewScopeResolver(new ScopeStore(caller));

        var result = await resolver.ResolveAsync(
            new ViewRequestProfile(caller.Policy.ProfileId, "RestrictedProfile"),
            ViewScopeRequest.Mine);

        Assert.Null(result);
    }

    private static ViewScopeStoreEntry State(bool access, bool include)
    {
        var profileId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        return new ViewScopeStoreEntry(
            new ViewProfilePolicy(profileId, true, access, include, false, true, now),
            new ViewPersonalSpace(Guid.NewGuid(), profileId, Guid.NewGuid(), now, now));
    }

    internal sealed class ScopeStore(params ViewScopeStoreEntry[] profiles) : IViewScopeStore
    {
        public Task<ViewScopeStoreEntry?> FindProfileAsync(Guid profileId, CancellationToken ct = default) =>
            Task.FromResult(profiles.FirstOrDefault(profile => profile.Policy.ProfileId == profileId));

        public Task<IReadOnlyList<ViewScopeStoreEntry>> GetProfilesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ViewScopeStoreEntry>>(profiles);
    }
}
