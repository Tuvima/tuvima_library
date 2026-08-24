using MediaEngine.Api.Services.View;

namespace MediaEngine.Api.Tests;

public sealed class ViewResourceAuthorizationTests
{
    [Theory]
    [InlineData(ViewResourceKind.Asset)]
    [InlineData(ViewResourceKind.Thumbnail)]
    [InlineData(ViewResourceKind.Original)]
    public async Task DerivativeAndOriginalDecisionsUseTheResolvedAssetScope(ViewResourceKind kind)
    {
        var caller = State(access: true, include: false);
        var sharedOwner = State(access: false, include: true);
        var resourceId = Guid.NewGuid();
        var service = Create(
            [caller, sharedOwner],
            new ViewResourceDescriptor(
                kind,
                resourceId,
                sharedOwner.ProfileId,
                sharedOwner.PersonalLibraryId));

        var allowed = await service.AuthorizeAsync(
            Identity(caller),
            new ViewResourceRequest(ViewScopeRequest.Shared, kind, resourceId));
        var denied = await service.AuthorizeAsync(
            Identity(caller),
            new ViewResourceRequest(ViewScopeRequest.Mine, kind, resourceId));

        Assert.True(allowed.IsAllowed);
        Assert.Equal(ViewAccessOutcome.NotFound, denied.Outcome);
    }

    [Fact]
    public async Task SearchReceivesOnlyAuthorizedScopeLibraries()
    {
        var caller = State(access: true, include: false);
        var included = State(access: false, include: true);
        var excluded = State(access: false, include: false);
        var service = Create([caller, included, excluded]);

        var decision = await service.AuthorizeAsync(
            Identity(caller),
            new ViewResourceRequest(ViewScopeRequest.Shared, ViewResourceKind.Search, null));

        Assert.True(decision.IsAllowed);
        Assert.Equal([included.PersonalLibraryId!.Value], decision.Scope!.LibraryIds);
    }

    [Fact]
    public async Task SharedGalleryRequiresGalleryGrantAndAnAuthorizedOwnerScope()
    {
        var caller = State(access: true, include: false);
        var owner = State(access: false, include: true);
        var galleryId = Guid.NewGuid();
        var shared = new ViewResourceDescriptor(
            ViewResourceKind.Gallery,
            galleryId,
            owner.ProfileId,
            owner.PersonalLibraryId,
            new HashSet<Guid> { caller.ProfileId });
        var service = Create([caller, owner], shared);

        var read = await service.AuthorizeAsync(
            Identity(caller),
            new ViewResourceRequest(ViewScopeRequest.Shared, ViewResourceKind.Gallery, galleryId));
        var contribute = await service.AuthorizeAsync(
            Identity(caller),
            new ViewResourceRequest(
                ViewScopeRequest.Shared,
                ViewResourceKind.Gallery,
                galleryId,
                ViewResourceAction.Contribute));

        Assert.True(read.IsAllowed);
        Assert.Equal(ViewAccessOutcome.NotFound, contribute.Outcome);
    }

    [Fact]
    public async Task GalleryGrantRemainsIndependentOfOwnerSharedInclusion()
    {
        var caller = State(access: true, include: false);
        var owner = State(access: false, include: false);
        var galleryId = Guid.NewGuid();
        var service = Create(
            [caller, owner],
            new ViewResourceDescriptor(
                ViewResourceKind.Gallery,
                galleryId,
                owner.ProfileId,
                owner.PersonalLibraryId,
                new HashSet<Guid> { caller.ProfileId }));

        var decision = await service.AuthorizeAsync(
            Identity(caller),
            new ViewResourceRequest(ViewScopeRequest.Shared, ViewResourceKind.Gallery, galleryId));

        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public async Task ExplicitGalleryShareDoesNotRequireSharedViewAccess()
    {
        var caller = State(access: false, include: false);
        var owner = State(access: false, include: false);
        var galleryId = Guid.NewGuid();
        var service = Create(
            [caller, owner],
            new ViewResourceDescriptor(
                ViewResourceKind.Gallery,
                galleryId,
                owner.ProfileId,
                owner.PersonalLibraryId,
                new HashSet<Guid> { caller.ProfileId }));

        var decision = await service.AuthorizeAsync(
            Identity(caller),
            new ViewResourceRequest(ViewScopeRequest.Mine, ViewResourceKind.Gallery, galleryId));

        Assert.True(decision.IsAllowed);
        Assert.Equal(ViewScopeKind.Mine, decision.Scope!.Kind);
    }

    [Theory]
    [InlineData(ViewResourceKind.Asset)]
    [InlineData(ViewResourceKind.Thumbnail)]
    [InlineData(ViewResourceKind.Original)]
    public async Task ExplicitGalleryShareCanAuthorizeOnlyItsIndividualAssets(ViewResourceKind kind)
    {
        var caller = State(access: false, include: false);
        var owner = State(access: false, include: false);
        var assetId = Guid.NewGuid();
        var service = Create(
            [caller, owner],
            new ViewResourceDescriptor(
                kind,
                assetId,
                owner.ProfileId,
                owner.PersonalLibraryId,
                new HashSet<Guid> { caller.ProfileId }));

        var decision = await service.AuthorizeAsync(
            Identity(caller),
            new ViewResourceRequest(ViewScopeRequest.Mine, kind, assetId));

        Assert.True(decision.IsAllowed);
        Assert.DoesNotContain(owner.PersonalLibraryId!.Value, decision.Scope!.LibraryIds);
    }

    [Fact]
    public async Task MissingTrustedCallerIsUnauthenticated()
    {
        var caller = State(access: true, include: true);
        var service = Create([caller]);

        var decision = await service.AuthorizeAsync(
            null,
            new ViewResourceRequest(ViewScopeRequest.Mine, ViewResourceKind.Search, null));

        Assert.Equal(ViewAccessOutcome.Unauthenticated, decision.Outcome);
    }

    private static ViewResourceAuthorizationService Create(
        ViewProfileScopeState[] profiles,
        params ViewResourceDescriptor[] resources) =>
        new(
            new ViewScopeResolver(new ViewScopeResolverTests.ScopeStore(profiles)),
            new ResourceStore(resources));

    private static ViewRequestProfile Identity(ViewProfileScopeState state) =>
        new(state.ProfileId, "Consumer");

    private static ViewProfileScopeState State(bool access, bool include) =>
        new(Guid.NewGuid(), true, access, include, Guid.NewGuid());

    private sealed class ResourceStore(params ViewResourceDescriptor[] resources) : IViewResourceStore
    {
        public Task<ViewResourceDescriptor?> FindAsync(
            ViewResourceKind kind,
            Guid resourceId,
            CancellationToken ct = default) =>
            Task.FromResult(resources.FirstOrDefault(resource =>
                resource.Kind == kind && resource.ResourceId == resourceId));
    }
}
