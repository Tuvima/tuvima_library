using MediaEngine.Api.Services.View;
using MediaEngine.Contracts.LocalAssets;
using MediaEngine.Domain.PersonalMedia;
using Microsoft.AspNetCore.Http;

namespace MediaEngine.Api.Tests;

public sealed class ViewQueryOrchestratorTests
{
    [Fact]
    public async Task QueryBackendReceivesOnlyResolverAuthorizedLibraries()
    {
        var caller = State(access: true, include: false);
        var included = State(access: false, include: true);
        var excluded = State(access: false, include: false);
        var http = new DefaultHttpContext();
        HttpViewRequestProfileContext.SetTrustedProfile(
            http,
            new ViewRequestProfile(caller.Policy.ProfileId, "Consumer"));
        var context = new HttpViewRequestProfileContext(
            new HttpContextAccessor { HttpContext = http });
        var resolver = new ViewScopeResolver(
            new ViewScopeResolverTests.ScopeStore(caller, included, excluded));
        var authorization = new ViewResourceAuthorizationService(resolver, new EmptyResourceStore());
        var backend = new CapturingBackend();
        var orchestrator = new ViewQueryOrchestrator(context, authorization, backend);

        var result = await orchestrator.QueryAsync(new ViewAssetQueryRequest(
            ViewScopeRequest.Shared,
            Search: "lake"));

        Assert.Equal(ViewAccessOutcome.Allowed, result.Outcome);
        var plan = Assert.IsType<ViewAssetQueryPlan>(backend.Plan);
        Assert.Equal([included.PersonalSpace!.LibraryId], plan.Scope.LibraryIds);
        Assert.Equal("lake", plan.Search);
    }

    [Fact]
    public async Task QueryDoesNotReachBackendWithoutTrustedProfileContext()
    {
        var context = new HttpViewRequestProfileContext(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext(),
        });
        var resolver = new ViewScopeResolver(new ViewScopeResolverTests.ScopeStore());
        var authorization = new ViewResourceAuthorizationService(resolver, new EmptyResourceStore());
        var backend = new CapturingBackend();
        var orchestrator = new ViewQueryOrchestrator(context, authorization, backend);

        var result = await orchestrator.QueryAsync(new ViewAssetQueryRequest(ViewScopeRequest.Mine));

        Assert.Equal(ViewAccessOutcome.Unauthenticated, result.Outcome);
        Assert.Null(backend.Plan);
    }

    [Fact]
    public async Task GalleryQueryDoesNotReachBackendUntilGalleryIsAuthorized()
    {
        var caller = State(access: false, include: false);
        var http = new DefaultHttpContext();
        HttpViewRequestProfileContext.SetTrustedProfile(
            http,
            new ViewRequestProfile(caller.Policy.ProfileId, "Consumer"));
        var context = new HttpViewRequestProfileContext(
            new HttpContextAccessor { HttpContext = http });
        var resolver = new ViewScopeResolver(new ViewScopeResolverTests.ScopeStore(caller));
        var authorization = new ViewResourceAuthorizationService(resolver, new EmptyResourceStore());
        var backend = new CapturingBackend();
        var orchestrator = new ViewQueryOrchestrator(context, authorization, backend);

        var result = await orchestrator.QueryAsync(new ViewAssetQueryRequest(
            ViewScopeRequest.Mine,
            GalleryId: Guid.NewGuid()));

        Assert.Equal(ViewAccessOutcome.NotFound, result.Outcome);
        Assert.Null(backend.Plan);
    }

    private static ViewScopeStoreEntry State(bool access, bool include)
    {
        var profileId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        return new ViewScopeStoreEntry(
            new ViewProfilePolicy(profileId, true, access, include, true, now),
            new ViewPersonalSpace(Guid.NewGuid(), profileId, Guid.NewGuid(), now, now));
    }

    private sealed class EmptyResourceStore : IViewResourceStore
    {
        public Task<ViewResourceDescriptor?> FindAsync(
            ViewResourceKind kind,
            Guid resourceId,
            Guid requestingProfileId,
            CancellationToken ct = default) => Task.FromResult<ViewResourceDescriptor?>(null);
    }

    private sealed class CapturingBackend : IViewAssetQueryBackend
    {
        public ViewAssetQueryPlan? Plan { get; private set; }

        public Task<ViewAssetTimelinePageDto> QueryAsync(
            ViewAssetQueryPlan plan,
            CancellationToken ct = default)
        {
            Plan = plan;
            return Task.FromResult(new ViewAssetTimelinePageDto([], null, false));
        }
    }
}
