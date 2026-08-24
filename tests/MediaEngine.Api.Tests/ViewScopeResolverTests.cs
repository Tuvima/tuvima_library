using MediaEngine.Api.Services.View;

namespace MediaEngine.Api.Tests;

public sealed class ViewScopeResolverTests
{
    [Fact]
    public async Task AccessAndInclusionAreIndependent()
    {
        var caller = State(access: true, include: false);
        var included = State(access: false, include: true);
        var excluded = State(access: true, include: false);
        var resolver = new ViewScopeResolver(new ScopeStore(caller, included, excluded));

        var resolution = Assert.IsType<ViewScopeResolution>(await resolver.ResolveAsync(
            new ViewRequestProfile(caller.ProfileId, "Consumer"),
            ViewScopeRequest.Shared));

        Assert.Equal(ViewScopeKind.Shared, resolution.Scope.Kind);
        Assert.DoesNotContain(caller.PersonalLibraryId!.Value, resolution.Scope.LibraryIds);
        Assert.Contains(included.PersonalLibraryId!.Value, resolution.Scope.LibraryIds);
        Assert.DoesNotContain(excluded.PersonalLibraryId!.Value, resolution.Scope.LibraryIds);
        Assert.Contains(resolution.AvailableScopes, option =>
            option.Kind == ViewScopeKind.Profile && option.ProfileId == included.ProfileId);
        Assert.DoesNotContain(resolution.AvailableScopes, option => option.ProfileId == excluded.ProfileId);
    }

    [Fact]
    public async Task InclusionDoesNotGrantCallerSharedAccess()
    {
        var caller = State(access: false, include: true);
        var other = State(access: false, include: true);
        var resolver = new ViewScopeResolver(new ScopeStore(caller, other));

        var resolution = Assert.IsType<ViewScopeResolution>(await resolver.ResolveAsync(
            new ViewRequestProfile(caller.ProfileId, "Consumer"),
            ViewScopeRequest.Shared));

        Assert.Equal(ViewScopeKind.Mine, resolution.Scope.Kind);
        Assert.True(resolution.Scope.WasFallback);
        Assert.Equal([caller.PersonalLibraryId!.Value], resolution.Scope.LibraryIds);
        Assert.Single(resolution.AvailableScopes);
    }

    [Fact]
    public async Task RevokedSavedProfileScopeFallsBackToSharedWithoutEnumeratingIt()
    {
        var caller = State(access: true, include: true);
        var revoked = State(access: false, include: false);
        var resolver = new ViewScopeResolver(new ScopeStore(caller, revoked));

        var resolution = Assert.IsType<ViewScopeResolution>(await resolver.ResolveAsync(
            new ViewRequestProfile(caller.ProfileId, "Consumer"),
            ViewScopeRequest.ForProfile(revoked.ProfileId)));

        Assert.Equal(ViewScopeKind.Shared, resolution.Scope.Kind);
        Assert.True(resolution.Scope.WasFallback);
        Assert.DoesNotContain(resolution.AvailableScopes, option => option.ProfileId == revoked.ProfileId);
    }

    [Fact]
    public async Task DisabledCallerHasNoViewScope()
    {
        var caller = State(access: true, include: true) with { ViewEnabled = false };
        var resolver = new ViewScopeResolver(new ScopeStore(caller));

        var result = await resolver.ResolveAsync(
            new ViewRequestProfile(caller.ProfileId, "Consumer"),
            ViewScopeRequest.Mine);

        Assert.Null(result);
    }

    private static ViewProfileScopeState State(bool access, bool include) =>
        new(Guid.NewGuid(), true, access, include, Guid.NewGuid());

    internal sealed class ScopeStore(params ViewProfileScopeState[] profiles) : IViewScopeStore
    {
        public Task<ViewProfileScopeState?> FindProfileAsync(Guid profileId, CancellationToken ct = default) =>
            Task.FromResult(profiles.FirstOrDefault(profile => profile.ProfileId == profileId));

        public Task<IReadOnlyList<ViewProfileScopeState>> GetProfilesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ViewProfileScopeState>>(profiles);
    }
}
