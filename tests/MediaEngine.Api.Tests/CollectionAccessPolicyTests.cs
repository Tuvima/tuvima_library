using MediaEngine.Api.Models;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Api.Tests;

public sealed class CollectionAccessPolicyTests
{
    private static readonly Guid OwnerProfileId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherProfileId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void ResolveVisibility_MapsUserAndLibraryScopes()
    {
        var privateCollection = CreateCollection(CollectionScope.User, OwnerProfileId);
        var sharedCollection = CreateCollection(CollectionScope.Library);

        Assert.Equal(CollectionAccessPolicy.PrivateVisibility, CollectionAccessPolicy.ResolveVisibility(privateCollection));
        Assert.Equal(CollectionAccessPolicy.SharedVisibility, CollectionAccessPolicy.ResolveVisibility(sharedCollection));
    }

    [Fact]
    public void CanAccess_AllowsSharedAndOwnedPrivateCollectionsOnly()
    {
        var activeProfile = new Profile
        {
            Id = OwnerProfileId,
            Role = ProfileRole.Consumer,
        };

        var ownedPrivateCollection = CreateCollection(CollectionScope.User, OwnerProfileId);
        var otherPrivateCollection = CreateCollection(CollectionScope.User, OtherProfileId);
        var sharedCollection = CreateCollection(CollectionScope.Library);

        Assert.True(CollectionAccessPolicy.CanAccess(ownedPrivateCollection, activeProfile));
        Assert.False(CollectionAccessPolicy.CanAccess(otherPrivateCollection, activeProfile));
        Assert.True(CollectionAccessPolicy.CanAccess(sharedCollection, activeProfile));
    }

    [Fact]
    public void CanEdit_SharedCollectionsRequireCuratorOrAdministrator()
    {
        var sharedCollection = CreateCollection(CollectionScope.Library);

        var consumer = new Profile
        {
            Id = OwnerProfileId,
            Role = ProfileRole.Consumer,
        };
        var curator = new Profile
        {
            Id = OwnerProfileId,
            Role = ProfileRole.Curator,
        };

        Assert.False(CollectionAccessPolicy.CanEdit(sharedCollection, consumer));
        Assert.True(CollectionAccessPolicy.CanEdit(sharedCollection, curator));
    }

    [Fact]
    public void CanEdit_CuratedCollectionsRequiresAdministrator()
    {
        var curatedCollection = CreateCollection(
            CollectionScope.Library,
            collectionType: CollectionType.Custom);
        var curator = new Profile
        {
            Id = OwnerProfileId,
            Role = ProfileRole.Curator,
        };
        var administrator = new Profile
        {
            Id = OtherProfileId,
            Role = ProfileRole.Administrator,
        };

        Assert.False(CollectionAccessPolicy.CanManageCuratedCollections(curator));
        Assert.False(CollectionAccessPolicy.CanEdit(curatedCollection, curator));
        Assert.True(CollectionAccessPolicy.CanManageCuratedCollections(administrator));
        Assert.True(CollectionAccessPolicy.CanEdit(curatedCollection, administrator));
    }

    [Fact]
    public void ApplyVisibility_MapsPrivateAndSharedToExistingStorageFields()
    {
        var collection = new Collection();

        CollectionAccessPolicy.ApplyVisibility(collection, CollectionAccessPolicy.PrivateVisibility, OwnerProfileId);

        Assert.Equal(CollectionScope.User, collection.Scope);
        Assert.Equal(OwnerProfileId, collection.ProfileId);

        CollectionAccessPolicy.ApplyVisibility(collection, CollectionAccessPolicy.SharedVisibility, OwnerProfileId);

        Assert.Equal(CollectionScope.Library, collection.Scope);
        Assert.Null(collection.ProfileId);
    }

    private static Collection CreateCollection(
        CollectionScope scope,
        Guid? profileId = null,
        CollectionType collectionType = CollectionType.Collection)
    {
        var collection = new Collection();
        collection.ClassifyAs(collectionType);
        collection.SetVisibility(scope, profileId);
        return collection;
    }
}
