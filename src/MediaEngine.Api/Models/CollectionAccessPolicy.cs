using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Api.Models;

public static class CollectionAccessPolicy
{
    public const string PrivateVisibility = "private";
    public const string SharedVisibility = "shared";

    public static string NormalizeVisibility(string? visibility) =>
        string.Equals(visibility, SharedVisibility, StringComparison.OrdinalIgnoreCase)
            ? SharedVisibility
            : PrivateVisibility;

    public static string ResolveVisibility(Collection collection) =>
        collection.Scope == CollectionScope.Library
            ? SharedVisibility
            : PrivateVisibility;

    public static bool CanManageSharedCollections(Profile? profile) =>
        profile?.Role is ProfileRole.Administrator or ProfileRole.Curator;

    public static bool CanManageCuratedCollections(Profile? profile) =>
        profile?.Role is ProfileRole.Administrator;

    public static bool CanAccess(Collection collection, Profile? activeProfile)
    {
        if (collection.Scope == CollectionScope.Library)
            return true;

        return activeProfile is not null
            && collection.Scope == CollectionScope.User
            && collection.ProfileId == activeProfile.Id;
    }

    public static bool CanEdit(Collection collection, Profile? activeProfile)
    {
        if (activeProfile is null)
            return false;

        if (collection.CollectionType == CollectionType.Custom)
            return CanManageCuratedCollections(activeProfile);

        if (collection.Scope == CollectionScope.Library)
            return CanManageSharedCollections(activeProfile);

        return collection.Scope == CollectionScope.User
            && collection.ProfileId == activeProfile.Id;
    }

    public static bool IsManagedCollectionType(CollectionType collectionType) =>
        collectionType is CollectionType.Custom
            or CollectionType.Playlist
            or CollectionType.Smart
            or CollectionType.PlaylistFolder;

    public static bool IsManagedCollectionType(string collectionType) =>
        string.Equals(collectionType, "Custom", StringComparison.OrdinalIgnoreCase)
        || string.Equals(collectionType, "Playlist", StringComparison.OrdinalIgnoreCase)
        || string.Equals(collectionType, "Smart", StringComparison.OrdinalIgnoreCase)
        || string.Equals(collectionType, "PlaylistFolder", StringComparison.OrdinalIgnoreCase);

    public static void ApplyVisibility(Collection collection, string visibility, Guid? activeProfileId)
    {
        if (string.Equals(visibility, SharedVisibility, StringComparison.OrdinalIgnoreCase))
        {
            collection.SetVisibility(CollectionScope.Library, profileId: null);
            return;
        }

        collection.SetVisibility(CollectionScope.User, activeProfileId);
    }
}
