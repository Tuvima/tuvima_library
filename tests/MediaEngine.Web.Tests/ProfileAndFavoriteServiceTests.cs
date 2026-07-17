using Bunit;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Integration;
using MediaEngine.Web.Services.Playback;
using MediaEngine.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace MediaEngine.Web.Tests;

public sealed class ProfileAndFavoriteServiceTests : TestContext
{
    private static readonly Guid ProfileId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public ProfileAndFavoriteServiceTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task ActiveProfileSession_ConcurrentConsumersShareOneProfileRequest()
    {
        var profileRequests = 0;
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<List<ProfileViewModel>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = EngineApiClientStub.Create(stub =>
            stub.SetHandler(nameof(IEngineApiClient.GetProfilesAsync), _ =>
            {
                Interlocked.Increment(ref profileRequests);
                requestStarted.TrySetResult();
                return response.Task;
            }));
        var session = new ActiveProfileSessionService(
            Services.GetRequiredService<IJSRuntime>(),
            api);

        var pendingProfiles = Enumerable.Range(0, 32)
            .Select(_ => session.GetActiveProfileAsync())
            .ToList();

        await requestStarted.Task;
        Assert.Equal(1, profileRequests);

        response.SetResult([CreateProfile()]);
        var profiles = await Task.WhenAll(pendingProfiles);

        Assert.Equal(1, profileRequests);
        Assert.All(profiles, profile => Assert.Equal(ProfileId, profile?.Id));
    }

    [Fact]
    public async Task FavoriteMembership_ReadsAreSingleFlightAndNeverCreateACollection()
    {
        var managedCollectionRequests = 0;
        var createCollectionRequests = 0;
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<List<ManagedCollectionViewModel>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = EngineApiClientStub.Create(stub =>
        {
            stub.SetHandler(nameof(IEngineApiClient.GetManagedCollectionsAsync), _ =>
            {
                Interlocked.Increment(ref managedCollectionRequests);
                requestStarted.TrySetResult();
                return response.Task;
            });
            stub.SetHandler(nameof(IEngineApiClient.CreateCollectionAsync), _ =>
            {
                Interlocked.Increment(ref createCollectionRequests);
                return Task.FromResult(true);
            });
        });
        var favorites = new FavoriteService(api);

        var pendingMemberships = Enumerable.Range(0, 32)
            .Select(_ => favorites.GetMembershipAsync(Guid.NewGuid(), ProfileId))
            .ToList();

        await requestStarted.Task;
        Assert.Equal(1, managedCollectionRequests);
        Assert.Equal(0, createCollectionRequests);

        response.SetResult([]);
        var memberships = await Task.WhenAll(pendingMemberships);

        Assert.All(memberships, Assert.Null);
        Assert.Equal(1, managedCollectionRequests);
        Assert.Equal(0, createCollectionRequests);
    }

    [Fact]
    public async Task FavoriteToggle_CreatesMissingCollectionOnlyOnMutation()
    {
        var collectionCreated = false;
        var createCollectionRequests = 0;
        var addItemRequests = 0;
        var favoritesCollectionId = Guid.NewGuid();
        var api = EngineApiClientStub.Create(stub =>
        {
            stub.SetHandler(nameof(IEngineApiClient.GetManagedCollectionsAsync), _ =>
                Task.FromResult(collectionCreated
                    ? new List<ManagedCollectionViewModel>
                    {
                        new()
                        {
                            Id = favoritesCollectionId,
                            Name = "Favorites",
                            CollectionType = "Playlist",
                            ProfileId = ProfileId,
                        },
                    }
                    : []));
            stub.SetHandler(nameof(IEngineApiClient.CreateCollectionAsync), _ =>
            {
                collectionCreated = true;
                Interlocked.Increment(ref createCollectionRequests);
                return Task.FromResult(true);
            });
            stub.SetHandler(nameof(IEngineApiClient.GetCollectionItemsAsync), _ =>
                Task.FromResult(new List<CollectionItemViewModel>()));
            stub.SetHandler(nameof(IEngineApiClient.AddCollectionItemAsync), _ =>
            {
                Interlocked.Increment(ref addItemRequests);
                return Task.FromResult(true);
            });
        });
        var favorites = new FavoriteService(api);
        var workId = Guid.NewGuid();

        var initialMembership = await favorites.GetMembershipAsync(workId, ProfileId);
        Assert.Null(initialMembership);
        Assert.Equal(0, createCollectionRequests);

        await favorites.ToggleAsync(workId, ProfileId);

        Assert.Equal(1, createCollectionRequests);
        Assert.Equal(1, addItemRequests);
    }

    [Fact]
    public async Task FavoriteList_ReturnsSavedItemsWithoutCreatingACollection()
    {
        var favoritesCollectionId = Guid.NewGuid();
        var workId = Guid.NewGuid();
        var createCollectionRequests = 0;
        var api = EngineApiClientStub.Create(stub =>
        {
            stub.SetHandler(nameof(IEngineApiClient.GetManagedCollectionsAsync), _ =>
                Task.FromResult(new List<ManagedCollectionViewModel>
                {
                    new()
                    {
                        Id = favoritesCollectionId,
                        Name = "Favorites",
                        CollectionType = "Playlist",
                        ProfileId = ProfileId,
                    },
                }));
            stub.SetHandler(nameof(IEngineApiClient.GetCollectionItemsAsync), _ =>
                Task.FromResult(new List<CollectionItemViewModel>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        WorkId = workId,
                        Title = "Saved work",
                        MediaType = "Book",
                    },
                }));
            stub.SetHandler(nameof(IEngineApiClient.CreateCollectionAsync), _ =>
            {
                Interlocked.Increment(ref createCollectionRequests);
                return Task.FromResult(true);
            });
        });
        var favorites = new FavoriteService(api);

        var list = await favorites.GetListAsync(ProfileId);

        Assert.Equal(favoritesCollectionId, list.CollectionId);
        Assert.Collection(list.Items, item => Assert.Equal(workId, item.WorkId));
        Assert.Equal(0, createCollectionRequests);
    }

    private static ProfileViewModel CreateProfile() => new(
        ProfileId,
        "Test User",
        "#C9922E",
        "Administrator",
        DateTimeOffset.UtcNow);
}
