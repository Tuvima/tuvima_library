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
        };

        var ownedPrivateCollection = CreateCollection(CollectionScope.User, OwnerProfileId);
        var otherPrivateCollection = CreateCollection(CollectionScope.User, OtherProfileId);
        var sharedCollection = CreateCollection(CollectionScope.Library);

        Assert.True(CollectionAccessPolicy.CanAccess(ownedPrivateCollection, activeProfile));
        Assert.False(CollectionAccessPolicy.CanAccess(otherPrivateCollection, activeProfile));
        Assert.True(CollectionAccessPolicy.CanAccess(sharedCollection, activeProfile));
    }

    [Fact]
    public void CanEdit_SharedCollectionsRequiresExplicitWriteDecision()
    {
        var sharedCollection = CreateCollection(CollectionScope.Library);

        var activeProfile = new Profile { Id = OwnerProfileId };

        Assert.False(CollectionAccessPolicy.CanEdit(sharedCollection, activeProfile, hasCollectionsWrite: false));
        Assert.True(CollectionAccessPolicy.CanEdit(sharedCollection, activeProfile, hasCollectionsWrite: true));
        Assert.True(CollectionAccessPolicy.CanEdit(sharedCollection, activeProfile: null, hasCollectionsWrite: true));
    }

    [Fact]
    public void CanEdit_CuratedCollectionsRequiresExplicitWriteDecision()
    {
        var curatedCollection = CreateCollection(
            CollectionScope.Library,
            collectionType: CollectionType.Custom);
        var activeProfile = new Profile { Id = OwnerProfileId };

        Assert.False(CollectionAccessPolicy.CanManageCuratedCollections(hasCollectionsWrite: false));
        Assert.False(CollectionAccessPolicy.CanEdit(curatedCollection, activeProfile, hasCollectionsWrite: false));
        Assert.True(CollectionAccessPolicy.CanManageCuratedCollections(hasCollectionsWrite: true));
        Assert.True(CollectionAccessPolicy.CanEdit(curatedCollection, activeProfile, hasCollectionsWrite: true));
        Assert.True(CollectionAccessPolicy.CanEdit(curatedCollection, activeProfile: null, hasCollectionsWrite: true));
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
