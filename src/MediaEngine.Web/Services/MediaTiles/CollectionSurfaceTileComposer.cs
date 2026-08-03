using MediaEngine.Contracts.Collections;
using MediaEngine.Domain.Services;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Navigation;
using MudBlazor;

namespace MediaEngine.Web.Services.MediaTiles;

/// <summary>
/// Shared card composition for the Collections section. Collection pages stay
/// focused on browse state while this class owns the established group-card
/// artwork, count, route, and person presentation contract.
/// </summary>
public static class CollectionSurfaceTileComposer
{
    public static MediaTileViewModel FromCollection(CollectionManagementCatalogViewModel collection)
    {
        var navigationUrl = collection.Person is null
            ? $"/details/collection/{collection.Id:D}"
            : $"/details/person/{collection.Person.Id:D}";
        var previewImages = collection.ArtworkItems
            .Select(item => MediaTileArtworkUrl.Sized(item.CoverUrl, "s"))
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();
        var artworkStackItems = collection.ArtworkItems
            .Select(ToArtworkStackItem)
            .Where(item => !string.IsNullOrWhiteSpace(item.ImageUrl))
            .Take(4)
            .ToList();
        var primaryArtwork = PrimaryArtwork(collection);
        var primarySmall = MediaTileArtworkUrl.Sized(primaryArtwork, "s");
        var primaryMedium = MediaTileArtworkUrl.Sized(primaryArtwork, "m");
        var surface = MediaTileArtworkResolver.Resolve(
            MediaTileBucket.Other,
            MediaTilePresentation.Default,
            [new MediaTileArtworkVariant(ArtworkRole.Cover, primarySmall, primaryMedium)],
            preferLandscapeTile: true);
        var heroArtwork = HeroBackground(collection);
        var heroMedium = MediaTileArtworkUrl.Sized(heroArtwork, "m");
        var paletteArtwork = PaletteArtwork(collection);
        var accentColor = StringHelpers.FirstNonBlank(
            paletteArtwork?.PrimaryColor,
            paletteArtwork?.AccentColor,
            "var(--tl-accent-primary)")!;
        var secondaryAccentColor = StringHelpers.FirstNonBlank(
            paletteArtwork?.SecondaryColor,
            paletteArtwork?.AccentColor,
            paletteArtwork?.PrimaryColor,
            "#111827")!;

        return new MediaTileViewModel
        {
            Id = collection.Id,
            CollectionId = collection.Id,
            Title = collection.Name,
            Subtitle = CollectionSubtitle(collection),
            Description = collection.Description,
            CoverUrl = primaryArtwork,
            BackgroundUrl = heroArtwork,
            PreviewImages = previewImages,
            ArtworkStackItems = artworkStackItems,
            StatusText = collection.StatusLabel,
            MediaCounts = BuildMediaCounts(collection),
            GroupSummary = new MediaTileGroupSummaryViewModel
            {
                OwnedCount = collection.ItemCount,
                EarliestYear = collection.EarliestYear,
                LatestYear = collection.LatestYear,
                RelationshipLabel = CollectionRelationshipLabel(collection.CollectionType),
            },
            ContextLines = BuildContextLines(collection),
            HoverFacts =
            [
                .. BuildMediaCountLabels(collection),
                collection.IsGlobal ? "Published" : "Unpublished",
            ],
            MediaKind = "Collection",
            AccentColor = accentColor,
            SecondaryAccentColor = secondaryAccentColor,
            ArtworkPalette = collection.ArtworkPalette,
            Shape = surface.Shape,
            HoverArtworkShape = surface.HoverArtworkShape,
            Presentation = MediaTilePresentation.Default,
            SurfaceKind = surface.SurfaceKind,
            HoverLayout = surface.HoverLayout,
            TileTextMode = MediaTileTextMode.CoverOnly,
            TileImageUrl = surface.TileImageUrl,
            TileImageSrcSet = surface.TileImageSrcSet,
            HoverImageUrl = surface.HoverImageUrl,
            HoverImageSrcSet = surface.HoverImageSrcSet,
            HeroBackgroundImageUrl = heroMedium,
            NavigationUrl = navigationUrl,
            PrimaryNavigationUrl = navigationUrl,
            DetailsNavigationUrl = navigationUrl,
            PrimaryActionLabel = "Open",
            Person = collection.Person is null
                ? null
                : new MediaTilePersonViewModel
                {
                    Id = collection.Person.Id,
                    Name = collection.Person.Name,
                    ImageUrl = collection.Person.HeadshotUrl,
                    Roles = collection.Person.Roles,
                },
            CollectionKey = collection.CollectionType,
            PreviewTotalCount = collection.ItemCount,
            SortYear = collection.LatestYear ?? collection.EarliestYear ?? 0,
            SortTimestamp = collection.ModifiedAt ?? collection.CreatedAt,
            IsCollection = true,
            UseLandscapeGroupTile = true,
        };
    }

    public static MediaTileViewModel FromShelf(ContentGroupViewModel group, CollectionShelfKind kind)
    {
        var route = ShelfRoute(group, kind);
        var artworkItems = group.PreviewItems
            .Where(item => !string.IsNullOrWhiteSpace(item.ImageUrl))
            .Select(item => new ArtworkStackItem
            {
                Id = item.WorkId.ToString("D"),
                WorkId = item.WorkId,
                Title = item.Title,
                ImageUrl = MediaTileArtworkUrl.Sized(item.ImageUrl, "s") ?? item.ImageUrl,
                MediaType = group.PrimaryMediaType,
                NavigationUrl = MediaNavigation.ForMedia(group.PrimaryMediaType, item.WorkId),
                Shape = ToArtworkShape(item.Shape, group.PrimaryMediaType),
                Position = item.Position,
                Description = item.Description,
                Facts = item.Facts ?? [],
            })
            .Take(4)
            .ToList();
        var primaryArtwork = StringHelpers.FirstNonBlank(
            group.CoverUrl,
            artworkItems.FirstOrDefault()?.ImageUrl,
            group.BackgroundUrl,
            group.BannerUrl);
        var presentation = kind switch
        {
            CollectionShelfKind.TvShow => MediaTilePresentation.TvSeries,
            CollectionShelfKind.Album => MediaTilePresentation.Album,
            CollectionShelfKind.MovieSeries => MediaTilePresentation.MovieSeries,
            CollectionShelfKind.BookSeries => MediaTilePresentation.BookSeries,
            CollectionShelfKind.ComicVolume => MediaTilePresentation.ComicSeries,
            CollectionShelfKind.AudiobookSeries => MediaTilePresentation.AudiobookSeries,
            _ => MediaTilePresentation.Default,
        };
        var shape = kind == CollectionShelfKind.Album
            ? MediaTileShape.Square
            : kind == CollectionShelfKind.TvShow
                ? MediaTileShape.Portrait
                : MediaTileShape.Landscape;
        var countLabel = ShelfCountLabel(kind, group.WorkCount);

        return new MediaTileViewModel
        {
            Id = group.RootWorkId ?? group.CollectionId,
            CollectionId = group.CollectionId,
            WorkId = group.RootWorkId,
            Title = group.DisplayName,
            Subtitle = countLabel,
            Description = group.Description,
            CoverUrl = primaryArtwork,
            BackgroundUrl = group.BackgroundUrl,
            BannerUrl = group.BannerUrl,
            LogoUrl = group.LogoUrl,
            PreviewImages = artworkItems.Select(item => item.ImageUrl).ToList(),
            ArtworkStackItems = artworkItems,
            MediaCounts =
            [
                new MediaTileMediaCountViewModel(ShelfIcon(kind), ShelfCountNoun(kind), group.WorkCount),
            ],
            GroupSummary = new MediaTileGroupSummaryViewModel
            {
                OwnedCount = group.WorkCount,
                EarliestYear = group.EarliestYear,
                LatestYear = group.LatestYear,
                RelationshipLabel = ShelfLabel(kind),
            },
            HoverFacts =
            [
                countLabel,
                .. string.IsNullOrWhiteSpace(group.Year) ? [] : new[] { group.Year },
            ],
            MediaKind = ShelfLabel(kind),
            AccentColor = group.MediaTypeColor,
            SecondaryAccentColor = "#111827",
            Shape = shape,
            HoverArtworkShape = kind == CollectionShelfKind.TvShow && !string.IsNullOrWhiteSpace(group.BackgroundUrl)
                ? MediaTileShape.Landscape
                : shape,
            Presentation = presentation,
            SurfaceKind = kind == CollectionShelfKind.Album
                ? MediaTileSurfaceKind.CoverSquare
                : kind == CollectionShelfKind.TvShow
                    ? MediaTileSurfaceKind.CoverPortrait
                    : MediaTileSurfaceKind.BannerLandscape,
            HoverLayout = kind == CollectionShelfKind.TvShow && !string.IsNullOrWhiteSpace(group.BackgroundUrl)
                ? MediaTileHoverLayout.BannerPopover
                : MediaTileHoverLayout.ArtOnlyPopover,
            TileTextMode = MediaTileTextMode.CoverOnly,
            TileImageUrl = MediaTileArtworkUrl.Sized(primaryArtwork, "s"),
            TileImageSrcSet = MediaTileArtworkUrl.SrcSet(
                MediaTileArtworkUrl.Sized(primaryArtwork, "s"),
                MediaTileArtworkUrl.Sized(primaryArtwork, "m")),
            HoverImageUrl = MediaTileArtworkUrl.Sized(group.BackgroundUrl ?? primaryArtwork, "m"),
            NavigationUrl = route,
            PrimaryNavigationUrl = route,
            DetailsNavigationUrl = route,
            PrimaryActionLabel = "Open",
            CollectionKey = kind.ToString(),
            PreviewTotalCount = group.WorkCount,
            SortYear = group.LatestYear ?? group.EarliestYear ?? ParseYear(group.Year),
            SortTimestamp = group.CreatedAt,
            IsCollection = true,
            UseLandscapeGroupTile = kind is not (CollectionShelfKind.TvShow or CollectionShelfKind.Album),
        };
    }

    private static string ShelfRoute(ContentGroupViewModel group, CollectionShelfKind kind) => kind switch
    {
        CollectionShelfKind.BookSeries => $"/details/bookseries/{group.CollectionId:D}?context=read",
        CollectionShelfKind.ComicVolume => $"/details/comicseries/{group.CollectionId:D}?context=comics",
        CollectionShelfKind.MovieSeries => $"/details/movieseries/{group.CollectionId:D}?context=watch",
        CollectionShelfKind.TvShow => $"/details/tvshow/{(group.RootWorkId ?? group.CollectionId):D}?context=watch",
        CollectionShelfKind.Album => $"/details/musicalbum/{(group.RootWorkId ?? group.CollectionId):D}?context=listen",
        CollectionShelfKind.AudiobookSeries => $"/details/collection/{group.CollectionId:D}?context=listen",
        _ => $"/details/collection/{group.CollectionId:D}",
    };

    private static IReadOnlyList<string> BuildContextLines(CollectionManagementCatalogViewModel collection)
    {
        var lines = new List<string>();
        if (collection.ArtworkItems.Count > 0)
        {
            lines.Add(string.Join(" / ", collection.ArtworkItems.Take(3).Select(item => item.Title)));
        }

        if (!string.IsNullOrWhiteSpace(collection.Description))
        {
            lines.Add(collection.Description);
        }

        return lines;
    }

    private static IReadOnlyList<MediaTileMediaCountViewModel> BuildMediaCounts(
        CollectionManagementCatalogViewModel collection)
    {
        var mediaTypeCounts = new[]
        {
            ToMediaCount("Movies", collection.MovieCount),
            ToMediaCount("TV", collection.TvCount),
            ToMediaCount("Books", collection.BookCount),
            ToMediaCount("Comics", collection.ComicCount),
            ToMediaCount("Music", collection.MusicCount),
            ToMediaCount("Audiobooks", collection.AudiobookCount),
        }
            .Where(count => count.Count > 0)
            .ToList();
        if (mediaTypeCounts.Count > 0)
        {
            if (collection.OtherCount > 0)
            {
                mediaTypeCounts.Add(new MediaTileMediaCountViewModel(
                    Icons.Material.Filled.MoreHoriz,
                    "Other",
                    collection.OtherCount));
            }

            return mediaTypeCounts;
        }

        var aggregateCounts = new List<MediaTileMediaCountViewModel>();
        if (collection.WatchCount > 0)
            aggregateCounts.Add(new MediaTileMediaCountViewModel(Icons.Material.Filled.PlayArrow, "Watch", collection.WatchCount));
        if (collection.ReadCount > 0)
            aggregateCounts.Add(new MediaTileMediaCountViewModel(Icons.Material.Filled.MenuBook, "Read", collection.ReadCount));
        if (collection.ListenCount > 0)
            aggregateCounts.Add(new MediaTileMediaCountViewModel(Icons.Material.Filled.Headphones, "Listen", collection.ListenCount));
        if (collection.OtherCount > 0)
            aggregateCounts.Add(new MediaTileMediaCountViewModel(Icons.Material.Filled.MoreHoriz, "Other", collection.OtherCount));
        if (aggregateCounts.Count > 0)
            return aggregateCounts;

        return collection.ArtworkItems
            .GroupBy(item => NormalizeMediaType(item.MediaType), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .Select(group => ToMediaCount(group.Key, group.Count()))
            .Where(count => count.Count > 0)
            .OrderBy(count => MediaCountSort(count.Label))
            .ToList();
    }

    private static string? CollectionRelationshipLabel(string? collectionType) => collectionType?.Trim() switch
    {
        "Universe" => "Shared universe",
        "Franchise" => "Franchise collection",
        "Series" => "Series rollup",
        "ContentGroup" => "Related works",
        "Custom" => "Curated collection",
        _ => null,
    };

    private static IReadOnlyList<string> BuildMediaCountLabels(CollectionManagementCatalogViewModel collection) =>
        BuildMediaCounts(collection)
            .Select(count => $"{count.Count} {count.Label}")
            .ToList();

    private static MediaTileMediaCountViewModel ToMediaCount(string mediaType, int count) => mediaType switch
    {
        "Movies" => new MediaTileMediaCountViewModel(Icons.Material.Filled.Movie, "Movies", count),
        "TV" => new MediaTileMediaCountViewModel(Icons.Material.Filled.LiveTv, "TV", count),
        "Music" => new MediaTileMediaCountViewModel(Icons.Material.Filled.MusicNote, "Music", count),
        "Audiobooks" => new MediaTileMediaCountViewModel(Icons.Material.Filled.Headphones, "Audiobooks", count),
        "Comics" => new MediaTileMediaCountViewModel(Icons.Material.Filled.AutoStories, "Comics", count),
        "Books" => new MediaTileMediaCountViewModel(Icons.Material.Filled.MenuBook, "Books", count),
        _ => new MediaTileMediaCountViewModel(Icons.Material.Filled.Theaters, "Media", count),
    };

    private static string NormalizeMediaType(string? mediaType)
    {
        var value = mediaType ?? string.Empty;
        if (value.Contains("tv", StringComparison.OrdinalIgnoreCase) || value.Contains("show", StringComparison.OrdinalIgnoreCase))
            return "TV";
        if (value.Contains("movie", StringComparison.OrdinalIgnoreCase) || value.Contains("film", StringComparison.OrdinalIgnoreCase))
            return "Movies";
        if (value.Contains("audio", StringComparison.OrdinalIgnoreCase))
            return "Audiobooks";
        if (value.Contains("music", StringComparison.OrdinalIgnoreCase)
            || value.Contains("song", StringComparison.OrdinalIgnoreCase)
            || value.Contains("album", StringComparison.OrdinalIgnoreCase))
            return "Music";
        if (value.Contains("comic", StringComparison.OrdinalIgnoreCase))
            return "Comics";
        if (value.Contains("book", StringComparison.OrdinalIgnoreCase) || value.Contains("epub", StringComparison.OrdinalIgnoreCase))
            return "Books";

        return string.Empty;
    }

    private static int MediaCountSort(string label) => label switch
    {
        "Movies" => 0,
        "TV" => 1,
        "Books" => 2,
        "Comics" => 3,
        "Music" => 4,
        "Audiobooks" => 5,
        _ => 6,
    };

    private static string CollectionSubtitle(CollectionManagementCatalogViewModel collection) =>
        string.Join(" / ", BuildMediaCountLabels(collection));

    private static string? PrimaryArtwork(CollectionManagementCatalogViewModel collection) =>
        StringHelpers.FirstNonBlank(collection.SquareArtworkUrl, collection.ArtworkItems.FirstOrDefault()?.CoverUrl);

    private static string? HeroBackground(CollectionManagementCatalogViewModel collection) =>
        StringHelpers.FirstNonBlank(collection.ArtworkItems.Skip(1).FirstOrDefault()?.CoverUrl, PrimaryArtwork(collection));

    private static ArtworkStackItem ToArtworkStackItem(CollectionArtworkItemDto item) => new()
    {
        Id = item.WorkId.ToString("D"),
        WorkId = item.WorkId,
        Title = item.Title,
        ImageUrl = MediaTileArtworkUrl.Sized(item.CoverUrl, "s") ?? string.Empty,
        MediaType = item.MediaType,
        NavigationUrl = MediaNavigation.ForMedia(item.MediaType, item.WorkId),
        Shape = ToArtworkShape(item.ArtworkShape, item.MediaType),
        Description = item.Description,
        Facts = item.Facts,
    };

    private static ArtworkShape ToArtworkShape(string? shape, string? mediaType)
    {
        if (string.Equals(shape, "square", StringComparison.OrdinalIgnoreCase))
            return ArtworkShape.Square;
        if (string.Equals(shape, "wide", StringComparison.OrdinalIgnoreCase)
            || string.Equals(shape, "landscape", StringComparison.OrdinalIgnoreCase))
            return ArtworkShape.Wide;
        if (string.Equals(shape, "portrait", StringComparison.OrdinalIgnoreCase))
            return ArtworkShape.Portrait;

        var normalized = NormalizeMediaType(mediaType);
        return normalized is "Audiobooks" or "Music"
            ? ArtworkShape.Square
            : ArtworkShape.Portrait;
    }

    private static CollectionArtworkItemDto? PaletteArtwork(CollectionManagementCatalogViewModel collection)
    {
        var candidates = collection.ArtworkItems
            .Where(item => !string.IsNullOrWhiteSpace(item.PrimaryColor)
                || !string.IsNullOrWhiteSpace(item.SecondaryColor)
                || !string.IsNullOrWhiteSpace(item.AccentColor))
            .OrderBy(item => StableHash(collection.Id, item.WorkId))
            .ToList();
        if (candidates.Count == 0)
            return null;

        var index = (StableHash(collection.Id, collection.Id) & int.MaxValue) % candidates.Count;
        return candidates[index];
    }

    private static int StableHash(Guid collectionId, Guid valueId)
    {
        var bytes = collectionId.ToByteArray().Concat(valueId.ToByteArray()).ToArray();
        unchecked
        {
            var hash = 17;
            foreach (var value in bytes)
                hash = (hash * 31) + value;
            return hash;
        }
    }

    private static string ShelfLabel(CollectionShelfKind kind) => kind switch
    {
        CollectionShelfKind.BookSeries => "Book series",
        CollectionShelfKind.ComicVolume => "Comic volume",
        CollectionShelfKind.MovieSeries => "Movie series",
        CollectionShelfKind.TvShow => "TV show",
        CollectionShelfKind.Album => "Album",
        CollectionShelfKind.AudiobookSeries => "Audiobook series",
        _ => "Shelf",
    };

    private static string ShelfCountNoun(CollectionShelfKind kind) => kind switch
    {
        CollectionShelfKind.ComicVolume => "Issues",
        CollectionShelfKind.TvShow => "Episodes",
        CollectionShelfKind.Album => "Tracks",
        _ => "Titles",
    };

    private static string ShelfCountLabel(CollectionShelfKind kind, int count)
    {
        var noun = ShelfCountNoun(kind);
        return $"{count} {(count == 1 ? noun.TrimEnd('s') : noun).ToLowerInvariant()}";
    }

    private static string ShelfIcon(CollectionShelfKind kind) => kind switch
    {
        CollectionShelfKind.BookSeries => Icons.Material.Outlined.MenuBook,
        CollectionShelfKind.ComicVolume => Icons.Material.Outlined.AutoStories,
        CollectionShelfKind.MovieSeries => Icons.Material.Outlined.Movie,
        CollectionShelfKind.TvShow => Icons.Material.Outlined.LiveTv,
        CollectionShelfKind.Album => Icons.Material.Outlined.Album,
        CollectionShelfKind.AudiobookSeries => Icons.Material.Outlined.Headphones,
        _ => Icons.Material.Outlined.ViewCarousel,
    };

    private static int ParseYear(string? value) =>
        int.TryParse(value, out var year) ? year : 0;
}

public enum CollectionShelfKind
{
    BookSeries,
    ComicVolume,
    MovieSeries,
    TvShow,
    Album,
    AudiobookSeries,
}
