using MediaEngine.Contracts.Collections;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Models;

namespace MediaEngine.Api.Models;

internal static class ManagedCollectionMapper
{
    public static ManagedCollectionDto FromDomain(
        Collection collection,
        int itemCount,
        Profile? activeProfile) => new()
    {
        Id = collection.Id,
        Name = collection.DisplayName ?? $"Collection {collection.Id.ToString("N")[..8]}",
        Description = collection.Description,
        IconName = collection.IconName,
        CoverArtworkUrl = string.IsNullOrWhiteSpace(collection.CoverArtworkPath)
            ? null
            : activeProfile is null
                ? $"/collections/{collection.Id}/artwork/poster"
                : $"/collections/{collection.Id}/artwork/poster?profileId={activeProfile.Id:D}",
        BackgroundArtworkUrl = string.IsNullOrWhiteSpace(collection.BackgroundArtworkPath) ? null
            : activeProfile is null ? $"/collections/{collection.Id}/artwork/background" : $"/collections/{collection.Id}/artwork/background?profileId={activeProfile.Id:D}",
        BannerArtworkUrl = string.IsNullOrWhiteSpace(collection.BannerArtworkPath) ? null
            : activeProfile is null ? $"/collections/{collection.Id}/artwork/banner" : $"/collections/{collection.Id}/artwork/banner?profileId={activeProfile.Id:D}",
        LogoArtworkUrl = string.IsNullOrWhiteSpace(collection.LogoArtworkPath) ? null
            : activeProfile is null ? $"/collections/{collection.Id}/artwork/logo" : $"/collections/{collection.Id}/artwork/logo?profileId={activeProfile.Id:D}",
        CollectionType = collection.CollectionType.ToStorageValue(),
        Scope = collection.Scope.ToStorageValue(),
        ProfileId = collection.ProfileId,
        Visibility = CollectionAccessPolicy.ResolveVisibility(collection),
        IsEnabled = collection.IsEnabled,
        IsFeatured = collection.IsFeatured,
        MinItems = collection.MinItems,
        RuleJson = collection.RuleJson,
        Resolution = collection.Resolution.ToStorageValue(),
        RuleHash = collection.RuleHash,
        MatchMode = collection.MatchMode.ToStorageValue(),
        SortField = collection.SortField,
        SortDirection = collection.SortDirection.ToStorageValue(),
        SecondarySortField = collection.SecondarySortField,
        SecondarySortDirection = collection.SecondarySortDirection?.ToStorageValue(),
        RefreshSchedule = collection.RefreshSchedule,
        ItemCount = itemCount,
        Status = !collection.IsEnabled ? "Disabled" : itemCount == 0 ? "Empty" : "Active",
        CreatedAt = collection.CreatedAt,
        ModifiedAt = collection.ModifiedAt,
        CanEdit = CollectionAccessPolicy.CanEdit(collection, activeProfile),
        CanShare = CollectionAccessPolicy.CanManageSharedCollections(activeProfile),
    };

    public static CollectionManagementCatalogDto ToCatalog(
        Collection collection,
        int itemCount,
        Profile? activeProfile,
        CollectionCatalogClassification classification,
        CollectionMediaCounts mediaCounts,
        IReadOnlyList<CollectionArtworkItemDto>? artworkItems = null,
        ArtworkPalette? artworkPalette = null,
        string? displayNameOverride = null,
        CollectionCatalogPersonDto? person = null)
    {
        var baseDto = FromDomain(collection, itemCount, activeProfile);
        var isGlobal = string.Equals(baseDto.Visibility, CollectionAccessPolicy.SharedVisibility, StringComparison.OrdinalIgnoreCase);
        var canEdit = CollectionAccessPolicy.CanEdit(collection, activeProfile);
        var canManageGlobal = CollectionAccessPolicy.CanManageSharedCollections(activeProfile);

        return new CollectionManagementCatalogDto
        {
            Id = baseDto.Id,
            Name = string.IsNullOrWhiteSpace(displayNameOverride) ? baseDto.Name : displayNameOverride,
            Description = baseDto.Description,
            IconName = baseDto.IconName,
            CoverArtworkUrl = baseDto.CoverArtworkUrl,
            BackgroundArtworkUrl = baseDto.BackgroundArtworkUrl,
            BannerArtworkUrl = baseDto.BannerArtworkUrl,
            LogoArtworkUrl = baseDto.LogoArtworkUrl,
            CollectionType = classification.CollectionType,
            Scope = baseDto.Scope,
            ProfileId = baseDto.ProfileId,
            Visibility = baseDto.Visibility,
            IsEnabled = baseDto.IsEnabled,
            IsFeatured = baseDto.IsFeatured,
            MinItems = baseDto.MinItems,
            RuleJson = baseDto.RuleJson,
            Resolution = baseDto.Resolution,
            RuleHash = baseDto.RuleHash,
            MatchMode = baseDto.MatchMode,
            SortField = baseDto.SortField,
            SortDirection = baseDto.SortDirection,
            SecondarySortField = baseDto.SecondarySortField,
            SecondarySortDirection = baseDto.SecondarySortDirection,
            RefreshSchedule = baseDto.RefreshSchedule,
            ItemCount = baseDto.ItemCount,
            Status = baseDto.Status,
            CreatedAt = baseDto.CreatedAt,
            ModifiedAt = baseDto.ModifiedAt,
            CanEdit = canEdit,
            CanShare = canManageGlobal,
            Family = classification.Family,
            SystemKey = classification.SystemKey,
            PrimaryLane = classification.PrimaryLaneOverride ?? mediaCounts.PrimaryLane,
            IsGlobal = isGlobal,
            IsSystem = classification.IsSystem,
            IsCrossMedia = classification.PrimaryLaneOverride is null && mediaCounts.IsCrossMedia,
            WatchCount = mediaCounts.WatchCount,
            ListenCount = mediaCounts.ListenCount,
            ReadCount = mediaCounts.ReadCount,
            OtherCount = mediaCounts.OtherCount,
            MovieCount = mediaCounts.MovieCount,
            TvCount = mediaCounts.TvCount,
            BookCount = mediaCounts.BookCount,
            ComicCount = mediaCounts.ComicCount,
            MusicCount = mediaCounts.MusicCount,
            AudiobookCount = mediaCounts.AudiobookCount,
            EarliestYear = mediaCounts.EarliestYear,
            LatestYear = mediaCounts.LatestYear,
            CanDelete = canEdit && !classification.IsSystem && CollectionAccessPolicy.IsManagedCollectionType(collection.CollectionType),
            CanRename = canEdit && !classification.IsSystem,
            CanToggleGlobal = canManageGlobal && !classification.IsSystem && CollectionAccessPolicy.IsManagedCollectionType(collection.CollectionType),
            ArtworkItems = artworkItems ?? [],
            ArtworkPalette = artworkPalette ?? ArtworkPalette.TuvimaDefault(),
            Person = person,
        };
    }
}
