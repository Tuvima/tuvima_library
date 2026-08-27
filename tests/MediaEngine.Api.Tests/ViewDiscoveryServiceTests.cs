using MediaEngine.Api.Services.View;
using MediaEngine.Domain.PersonalMedia;
using MediaEngine.Storage.Contracts;
using Microsoft.AspNetCore.Http;

namespace MediaEngine.Api.Tests;

public sealed class ViewDiscoveryServiceTests
{
    [Fact]
    public async Task UnauthorizedProfileScopeFallsBackWithoutSendingPrivateLibraryToStorage()
    {
        var caller = State(access: true, include: true);
        var included = State(access: false, include: true);
        var privateProfile = State(access: false, include: false);
        var repository = new CapturingRepository();
        var service = Service(repository, caller, caller, included, privateProfile);

        var result = await service.GetPlacesAsync(new ViewDiscoveryRequest(
            ViewScopeRequest.ForProfile(privateProfile.Policy.ProfileId), 50));

        Assert.Equal(ViewAccessOutcome.Allowed, result.Outcome);
        Assert.True(result.Scope!.WasFallback);
        Assert.Equal(ViewScopeKind.Shared, result.Scope.Kind);
        Assert.Contains(included.PersonalSpace!.LibraryId, repository.PlaceQuery!.AuthorizedLibraryIds);
        Assert.Contains(caller.PersonalSpace!.LibraryId, repository.PlaceQuery.AuthorizedLibraryIds);
        Assert.DoesNotContain(privateProfile.PersonalSpace!.LibraryId, repository.PlaceQuery.AuthorizedLibraryIds);
    }

    [Fact]
    public async Task MineScopeComesFromTrustedCallerAndCannotBeReplacedByDirectLibraryIds()
    {
        var caller = State(access: false, include: false);
        var other = State(access: false, include: false);
        var repository = new CapturingRepository();
        var service = Service(repository, caller, caller, other);

        var result = await service.GetPeopleAsync(new ViewDiscoveryRequest(ViewScopeRequest.Mine, 25));

        Assert.Equal(ViewAccessOutcome.Allowed, result.Outcome);
        Assert.Equal([caller.PersonalSpace!.LibraryId], repository.PeopleQuery!.AuthorizedLibraryIds);
        Assert.DoesNotContain(other.PersonalSpace!.LibraryId, repository.PeopleQuery.AuthorizedLibraryIds);
    }

    [Fact]
    public async Task MissingTrustedIdentityNeverQueriesDiscoveryStorage()
    {
        var repository = new CapturingRepository();
        var service = Service(repository, caller: null, State(access: true, include: true));

        var result = await service.GetPlacesAsync(new ViewDiscoveryRequest(ViewScopeRequest.Shared, 50));

        Assert.Equal(ViewAccessOutcome.Unauthenticated, result.Outcome);
        Assert.Null(repository.PlaceQuery);
    }

    [Fact]
    public async Task PeopleCapabilityDoesNotClaimAutomaticFaceProcessing()
    {
        var caller = State(access: false, include: false);
        var repository = new CapturingRepository
        {
            PeoplePage = new ViewPeopleDiscoveryPage([], null, false, false),
        };
        var service = Service(repository, caller, caller);

        var result = await service.GetPeopleAsync(new ViewDiscoveryRequest(ViewScopeRequest.Mine, 25));

        Assert.False(result.Page!.Capability.AutomaticProcessingAvailable);
        Assert.Equal("empty", result.Page.Capability.State);
        Assert.Contains("not currently available", result.Page.Capability.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("mine", null, ViewScopeKind.Mine)]
    [InlineData("shared", null, ViewScopeKind.Shared)]
    [InlineData("profile", "4c683e9f-a208-49eb-a78f-08690d384846", ViewScopeKind.Profile)]
    public void EndpointScopeParserAcceptsOnlyFriendlyScopes(string scope, string? profile, ViewScopeKind expected)
    {
        var parsed = MediaEngine.Api.Endpoints.ViewDiscoveryEndpoints.ParseScope(
            scope,
            profile is null ? null : Guid.Parse(profile));
        Assert.Equal(expected, parsed.Kind);
    }

    private static ViewDiscoveryService Service(
        CapturingRepository repository,
        ViewScopeStoreEntry? caller,
        params ViewScopeStoreEntry[] profiles)
    {
        var http = new DefaultHttpContext();
        if (caller is not null)
        {
            HttpViewRequestProfileContext.SetTrustedProfile(
                http,
                new ViewRequestProfile(caller.Policy.ProfileId, "RestrictedProfile"));
        }
        var context = new HttpViewRequestProfileContext(new HttpContextAccessor { HttpContext = http });
        var authorization = new ViewResourceAuthorizationService(
            new ViewScopeResolver(new ViewScopeResolverTests.ScopeStore(profiles)),
            new UnusedResourceStore());
        return new ViewDiscoveryService(context, authorization, repository);
    }

    private static ViewScopeStoreEntry State(bool access, bool include)
    {
        var profileId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        return new ViewScopeStoreEntry(
            new ViewProfilePolicy(profileId, true, access, include, true, now),
            new ViewPersonalSpace(Guid.NewGuid(), profileId, Guid.NewGuid(), now, now));
    }

    private sealed class CapturingRepository : IViewDiscoveryRepository
    {
        public ViewPlaceDiscoveryQuery? PlaceQuery { get; private set; }
        public ViewPeopleDiscoveryQuery? PeopleQuery { get; private set; }
        public ViewPlaceDiscoveryPage PlacePage { get; init; } = new([], null, false, false);
        public ViewPeopleDiscoveryPage PeoplePage { get; init; } = new([], null, false, false);

        public ViewPlaceDiscoveryPage QueryPlaces(ViewPlaceDiscoveryQuery query, CancellationToken ct = default)
        {
            PlaceQuery = query;
            return PlacePage;
        }

        public ViewPeopleDiscoveryPage QueryPeople(ViewPeopleDiscoveryQuery query, CancellationToken ct = default)
        {
            PeopleQuery = query;
            return PeoplePage;
        }
    }

    private sealed class UnusedResourceStore : IViewResourceStore
    {
        public Task<ViewResourceDescriptor?> FindAsync(
            ViewResourceKind kind,
            Guid resourceId,
            Guid requestingProfileId,
            CancellationToken ct = default) => throw new InvalidOperationException("Search authorization must not resolve direct resources.");
    }
}
