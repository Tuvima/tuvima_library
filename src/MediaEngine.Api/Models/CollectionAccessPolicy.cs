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
        string.Equals(collection.Scope, "library", StringComparison.OrdinalIgnoreCase)
            ? SharedVisibility
            : PrivateVisibility;

    public static bool CanManageSharedCollections(Profile? profile) =>
        profile?.Role is ProfileRole.Administrator or ProfileRole.Curator;

    public static bool CanAccess(Collection collection, Profile? activeProfile)
    {
        if (string.Equals(collection.Scope, "library", StringComparison.OrdinalIgnoreCase))
            return true;

        return activeProfile is not null
            && string.Equals(collection.Scope, "user", StringComparison.OrdinalIgnoreCase)
            && collection.ProfileId == activeProfile.Id;
    }

    public static bool CanEdit(Collection collection, Profile? activeProfile)
    {
        if (activeProfile is null)
            return false;

        if (string.Equals(collection.Scope, "library", StringComparison.OrdinalIgnoreCase))
            return CanManageSharedCollections(activeProfile);

        return string.Equals(collection.Scope, "user", StringComparison.OrdinalIgnoreCase)
            && collection.ProfileId == activeProfile.Id;
    }

    public static bool IsManagedCollectionType(string collectionType) =>
        string.Equals(collectionType, "Custom", StringComparison.OrdinalIgnoreCase)
        || string.Equals(collectionType, "Playlist", StringComparison.OrdinalIgnoreCase);

    public static void ApplyVisibility(Collection collection, string visibility, Guid? activeProfileId)
    {
        if (string.Equals(visibility, SharedVisibility, StringComparison.OrdinalIgnoreCase))
        {
            collection.Scope = "library";
            collection.ProfileId = null;
            return;
        }

        collection.Scope = "user";
        collection.ProfileId = activeProfileId;
    }
}
