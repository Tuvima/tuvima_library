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
        var privateCollection = new Collection
        {
            Scope = "user",
            ProfileId = OwnerProfileId,
        };
        var sharedCollection = new Collection
        {
            Scope = "library",
            ProfileId = null,
        };

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

        var ownedPrivateCollection = new Collection
        {
            Scope = "user",
            ProfileId = OwnerProfileId,
        };
        var otherPrivateCollection = new Collection
        {
            Scope = "user",
            ProfileId = OtherProfileId,
        };
        var sharedCollection = new Collection
        {
            Scope = "library",
            ProfileId = null,
        };

        Assert.True(CollectionAccessPolicy.CanAccess(ownedPrivateCollection, activeProfile));
        Assert.False(CollectionAccessPolicy.CanAccess(otherPrivateCollection, activeProfile));
        Assert.True(CollectionAccessPolicy.CanAccess(sharedCollection, activeProfile));
    }

    [Fact]
    public void CanEdit_SharedCollectionsRequireCuratorOrAdministrator()
    {
        var sharedCollection = new Collection
        {
            Scope = "library",
            ProfileId = null,
        };

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
    public void ApplyVisibility_MapsPrivateAndSharedToExistingStorageFields()
    {
        var collection = new Collection();

        CollectionAccessPolicy.ApplyVisibility(collection, CollectionAccessPolicy.PrivateVisibility, OwnerProfileId);

        Assert.Equal("user", collection.Scope);
        Assert.Equal(OwnerProfileId, collection.ProfileId);

        CollectionAccessPolicy.ApplyVisibility(collection, CollectionAccessPolicy.SharedVisibility, OwnerProfileId);

        Assert.Equal("library", collection.Scope);
        Assert.Null(collection.ProfileId);
    }
}
