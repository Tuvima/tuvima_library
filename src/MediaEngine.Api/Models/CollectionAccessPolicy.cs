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
        collection.OwnerKind == ContainerOwnerKind.Library || collection.Audience == ContainerAudience.Everyone
            ? SharedVisibility
            : PrivateVisibility;

    public static bool CanManageSharedCollections(bool hasCollectionsWrite) =>
        hasCollectionsWrite;

    public static bool CanManageCuratedCollections(bool hasCollectionsWrite) =>
        hasCollectionsWrite;

    public static bool CanAccess(Collection collection, Profile? activeProfile)
    {
        if (collection.OwnerKind == ContainerOwnerKind.Library || collection.Audience == ContainerAudience.Everyone)
        {
            return true;
        }

        if (activeProfile is null || collection.Scope != CollectionScope.User)
        {
            return false;
        }

        return collection.ProfileId == activeProfile.Id
            || collection.Audience == ContainerAudience.SelectedProfiles
               && collection.AudienceProfileIds.Contains(activeProfile.Id);
    }

    public static bool CanEdit(
        Collection collection,
        Profile? activeProfile,
        bool hasCollectionsWrite)
    {
        if (!hasCollectionsWrite)
        {
            return false;
        }

        if (collection.OwnerKind == ContainerOwnerKind.Library)
        {
            return CanManageCuratedCollections(hasCollectionsWrite);
        }

        if (collection.Scope == CollectionScope.Library)
        {
            return CanManageSharedCollections(hasCollectionsWrite);
        }

        return activeProfile is not null
            && collection.Scope == CollectionScope.User
            && collection.ProfileId == activeProfile.Id;
    }

    public static bool IsManagedCollectionType(CollectionType collectionType) =>
        collectionType is CollectionType.Custom
            or CollectionType.Playlist
            or CollectionType.Smart // Legacy rows remain editable and are normalized by the editor/API.
            or CollectionType.PlaylistFolder;

    public static bool IsManagedCollectionType(string collectionType) =>
        string.Equals(collectionType, "Custom", StringComparison.OrdinalIgnoreCase)
        || string.Equals(collectionType, "Playlist", StringComparison.OrdinalIgnoreCase)
        || string.Equals(collectionType, "Smart", StringComparison.OrdinalIgnoreCase)
        || string.Equals(collectionType, "PlaylistFolder", StringComparison.OrdinalIgnoreCase);

    public static void ApplyOwnership(Collection collection, Guid? activeProfileId)
    {
        if (collection.OwnerKind == ContainerOwnerKind.Library)
        {
            collection.Audience = ContainerAudience.Everyone;
            collection.SetVisibility(CollectionScope.Library, profileId: null);
            return;
        }

        collection.SetVisibility(CollectionScope.User, activeProfileId);
        if (collection.Audience == ContainerAudience.Everyone)
        {
            collection.Audience = ContainerAudience.Private;
        }
    }

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
