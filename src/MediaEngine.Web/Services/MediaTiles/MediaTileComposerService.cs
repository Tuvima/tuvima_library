using MediaEngine.Contracts.Display;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Integration;

namespace MediaEngine.Web.Services.MediaTiles;

public sealed class MediaTileComposerService
{
    private readonly IEngineApiClient _api;

    public MediaTileComposerService(IEngineApiClient api)
    {
        _api = api;
    }

    public async Task<DiscoveryPageViewModel> BuildHomeAsync(CancellationToken ct = default) =>
        FromDisplayPage(await RequireDisplayPageAsync(_api.GetDisplayHomeAsync(ct), "home"));

    public async Task<DiscoveryPageViewModel> BuildReadAsync(CancellationToken ct = default) =>
        FromDisplayPage(await RequireDisplayPageAsync(_api.GetDisplayBrowseAsync(lane: "read", grouping: "all", ct: ct), "read"));

    public async Task<DiscoveryPageViewModel> BuildWatchAsync(CancellationToken ct = default) =>
        FromDisplayPage(await RequireDisplayPageAsync(_api.GetDisplayBrowseAsync(lane: "watch", grouping: "all", ct: ct), "watch"));

    public async Task<DiscoveryPageViewModel> BuildListenAsync(CancellationToken ct = default) =>
        FromDisplayPage(await RequireDisplayPageAsync(_api.GetDisplayBrowseAsync(lane: "listen", grouping: "all", ct: ct), "listen"));

    public static DiscoveryPageViewModel FromDisplayPage(DisplayPageDto page)
    {
        var shelves = page.Shelves
            .Select(shelf => new MediaTileShelfViewModel
            {
                Title = shelf.Title,
                Subtitle = shelf.Subtitle,
                Items = shelf.Items.Select(FromDisplayCard).ToList(),
                SeeAllRoute = shelf.SeeAllRoute,
            })
            .ToList();

        var heroArtwork = page.Hero?.Artwork;
        var heroSurfaceKind = heroArtwork is null ? MediaTileSurfaceKind.BannerLandscape : ResolveHeroSurfaceKind(heroArtwork);
        var heroPreviewSurfaceKind = heroArtwork is null ? MediaTileSurfaceKind.CoverPortrait : ResolvePreviewSurfaceKind(heroArtwork);

        return new DiscoveryPageViewModel
        {
            Key = page.Key,
            AccentColor = "#1CE783",
            Hero = page.Hero is null ? null : new DiscoveryHeroViewModel
            {
                Eyebrow = page.Hero.Eyebrow ?? "From your library",
                Title = page.Hero.Title,
                Subtitle = page.Hero.Subtitle,
                BackgroundImageUrl = FirstNonBlank(page.Hero.Artwork.BackgroundLargeUrl, page.Hero.Artwork.BannerLargeUrl, page.Hero.Artwork.BackgroundMediumUrl, page.Hero.Artwork.BannerMediumUrl),
                HeroBackgroundImageUrl = FirstNonBlank(page.Hero.Artwork.BackgroundLargeUrl, page.Hero.Artwork.BannerLargeUrl, page.Hero.Artwork.BackgroundMediumUrl, page.Hero.Artwork.BannerMediumUrl),
                BannerImageUrl = FirstNonBlank(page.Hero.Artwork.BannerLargeUrl, page.Hero.Artwork.BannerMediumUrl),
                PreviewImageUrl = FirstNonBlank(page.Hero.Artwork.CoverLargeUrl, page.Hero.Artwork.SquareLargeUrl, page.Hero.Artwork.CoverMediumUrl, page.Hero.Artwork.SquareMediumUrl),
                SurfaceKind = heroSurfaceKind,
                PreviewSurfaceKind = heroPreviewSurfaceKind,
                LogoUrl = page.Hero.Artwork.LogoUrl,
                AccentColor = page.Hero.Artwork.AccentColor ?? "#1CE783",
                ProgressPct = page.Hero.Progress?.Percent,
                PrimaryActionLabel = page.Hero.Actions.FirstOrDefault()?.Label ?? "Open",
                PrimaryNavigationUrl = page.Hero.Actions.FirstOrDefault()?.WebUrl ?? "/",
                SecondaryActionLabel = "Details",
                SecondaryNavigationUrl = page.Hero.Actions.Skip(1).FirstOrDefault()?.WebUrl,
            },
            Shelves = shelves,
            Catalog = page.Catalog.Select(FromDisplayCard).ToList(),
            EmptyTitle = "Your home screen is waiting for its first story",
            EmptySubtitle = "Once media lands in the library, home becomes the personalized view across everything you own.",
        };
    }

    public static MediaTileViewModel FromDisplayCard(DisplayCardDto card)
    {
        var bucket = GetBucket(card.MediaType);
        var presentation = card.Presentation switch
        {
            "tvSeries" => MediaTilePresentation.TvSeries,
            "movieSeries" => MediaTilePresentation.MovieSeries,
            "bookSeries" => MediaTilePresentation.BookSeries,
            "comicSeries" => MediaTilePresentation.ComicSeries,
            "audiobookSeries" => MediaTilePresentation.AudiobookSeries,
            "album" => MediaTilePresentation.Album,
            "artist" => MediaTilePresentation.Artist,
            _ => MediaTilePresentation.Default,
        };

        var surface = MediaTileArtworkResolver.Resolve(
            bucket,
            presentation,
            [
                new MediaTileArtworkVariant(ArtworkRole.Background, card.Artwork.BackgroundSmallUrl, card.Artwork.BackgroundMediumUrl, card.Artwork.BackgroundLargeUrl, card.Artwork.BackgroundWidthPx, card.Artwork.BackgroundHeightPx),
                new MediaTileArtworkVariant(ArtworkRole.Banner, card.Artwork.BannerSmallUrl, card.Artwork.BannerMediumUrl, card.Artwork.BannerLargeUrl, card.Artwork.BannerWidthPx, card.Artwork.BannerHeightPx),
                new MediaTileArtworkVariant(ArtworkRole.Square, card.Artwork.SquareSmallUrl, card.Artwork.SquareMediumUrl, card.Artwork.SquareLargeUrl, card.Artwork.SquareWidthPx, card.Artwork.SquareHeightPx),
                new MediaTileArtworkVariant(ArtworkRole.Cover, card.Artwork.CoverSmallUrl, card.Artwork.CoverMediumUrl, card.Artwork.CoverLargeUrl, card.Artwork.CoverWidthPx, card.Artwork.CoverHeightPx),
            ]);

        return new MediaTileViewModel
        {
            Id = card.Id,
            WorkId = card.WorkId,
            CollectionId = card.CollectionId,
            Title = card.Title,
            Subtitle = card.Subtitle,
            CoverUrl = card.Artwork.CoverUrl,
            BackgroundUrl = card.Artwork.BackgroundUrl,
            BannerUrl = card.Artwork.BannerUrl,
            LogoUrl = card.Artwork.LogoUrl,
            MetaText = string.Join(" / ", card.Facts),
            HoverFacts = card.Facts,
            MediaKind = card.MediaType,
            AccentColor = card.Artwork.AccentColor ?? AccentForBucket(bucket),
            Shape = surface.Shape,
            Presentation = presentation,
            SurfaceKind = surface.SurfaceKind,
            HoverLayout = surface.HoverLayout,
            HoverMode = bucket is MediaTileBucket.Movie or MediaTileBucket.Tv ? MediaTileHoverMode.Expanded : MediaTileHoverMode.Preview,
            TileTextMode = string.Equals(card.TileTextMode, "coverOnly", StringComparison.OrdinalIgnoreCase)
                ? MediaTileTextMode.CoverOnly
                : MediaTileTextMode.Caption,
            PreviewPlacement = string.Equals(card.PreviewPlacement, "bottom", StringComparison.OrdinalIgnoreCase)
                ? MediaTilePreviewPlacement.Bottom
                : MediaTilePreviewPlacement.Smart,
            TileImageUrl = surface.TileImageUrl,
            TileImageSrcSet = surface.TileImageSrcSet,
            HoverImageUrl = surface.HoverImageUrl,
            HoverImageSrcSet = surface.HoverImageSrcSet,
            HeroBackgroundImageUrl = surface.HeroBackgroundImageUrl,
            PreviewImageUrl = surface.PreviewImageUrl,
            TileImageFitMode = surface.TileImageFitMode,
            HoverImageFitMode = surface.HoverImageFitMode,
            NavigationUrl = card.Actions.FirstOrDefault(action => !string.IsNullOrWhiteSpace(action.WebUrl))?.WebUrl ?? "/",
            DetailsNavigationUrl = card.Actions.Skip(1).FirstOrDefault(action => !string.IsNullOrWhiteSpace(action.WebUrl))?.WebUrl
                ?? card.Actions.FirstOrDefault(action => !string.IsNullOrWhiteSpace(action.WebUrl))?.WebUrl
                ?? "/",
            PrimaryNavigationUrl = card.Progress?.ResumeAction?.WebUrl
                ?? card.Actions.FirstOrDefault(action => !string.IsNullOrWhiteSpace(action.WebUrl))?.WebUrl,
            PrimaryActionLabel = card.Actions.FirstOrDefault()?.Label ?? "Open",
            ProgressPct = card.Progress?.Percent,
            ProgressLabel = card.Progress?.Label,
            Creator = card.Subtitle,
            SortTimestamp = card.SortTimestamp,
            IsCollection = card.Flags.IsCollection,
        };
    }

    private static async Task<DisplayPageDto> RequireDisplayPageAsync(Task<DisplayPageDto?> request, string surface)
    {
        var page = await request;
        return page ?? throw new InvalidOperationException($"Display API did not return a page for {surface}.");
    }

    private static MediaTileBucket GetBucket(string? mediaType)
    {
        var value = (mediaType ?? string.Empty).ToLowerInvariant();
        if (value.Contains("comic") || value.Contains("cbz") || value.Contains("cbr"))
        {
            return MediaTileBucket.Comic;
        }

        if (value.Contains("audiobook") || value.Contains("m4b"))
        {
            return MediaTileBucket.Audiobook;
        }

        if (value.Contains("music"))
        {
            return MediaTileBucket.Music;
        }

        if (value.Contains("movie") || value.Contains("video"))
        {
            return MediaTileBucket.Movie;
        }

        if (value.Contains("tv"))
        {
            return MediaTileBucket.Tv;
        }

        if (value.Contains("book") || value.Contains("epub"))
        {
            return MediaTileBucket.Book;
        }

        return MediaTileBucket.Other;
    }

    private static string AccentForBucket(MediaTileBucket bucket) => bucket switch
    {
        MediaTileBucket.Book => "var(--tl-media-book)",
        MediaTileBucket.Comic => "var(--tl-media-comic)",
        MediaTileBucket.Audiobook => "var(--tl-media-audio)",
        MediaTileBucket.Movie => "var(--tl-status-info)",
        MediaTileBucket.Tv => "var(--tl-media-video)",
        MediaTileBucket.Music => "var(--tl-media-audio)",
        _ => "var(--tl-accent-primary)",
    };

    private static MediaTileSurfaceKind ResolveHeroSurfaceKind(DisplayArtworkDto artwork)
    {
        if (HasUsableLandscape(artwork.BackgroundUrl, artwork.BackgroundWidthPx, artwork.BackgroundHeightPx)
            || HasUsableLandscape(artwork.BannerUrl, artwork.BannerWidthPx, artwork.BannerHeightPx))
        {
            return MediaTileSurfaceKind.BannerLandscape;
        }

        return ResolvePreviewSurfaceKind(artwork);
    }

    private static MediaTileSurfaceKind ResolvePreviewSurfaceKind(DisplayArtworkDto artwork)
    {
        if (!string.IsNullOrWhiteSpace(artwork.CoverUrl))
        {
            return ShapeFromDimensions(artwork.CoverWidthPx, artwork.CoverHeightPx);
        }

        if (!string.IsNullOrWhiteSpace(artwork.SquareUrl))
        {
            return ShapeFromDimensions(artwork.SquareWidthPx, artwork.SquareHeightPx, MediaTileSurfaceKind.CoverSquare);
        }

        if (!string.IsNullOrWhiteSpace(artwork.BannerUrl))
        {
            return MediaTileSurfaceKind.BannerLandscape;
        }

        return MediaTileSurfaceKind.CoverPortrait;
    }

    private static MediaTileSurfaceKind ShapeFromDimensions(int? width, int? height, MediaTileSurfaceKind fallback = MediaTileSurfaceKind.CoverPortrait)
    {
        if (width is not > 0 || height is not > 0)
        {
            return fallback;
        }

        var ratio = width.Value / (double)height.Value;
        if (ratio >= 1.45)
        {
            return MediaTileSurfaceKind.BannerLandscape;
        }

        return ratio >= 0.85
            ? MediaTileSurfaceKind.CoverSquare
            : MediaTileSurfaceKind.CoverPortrait;
    }

    private static bool HasUsableLandscape(string? url, int? width, int? height) =>
        !string.IsNullOrWhiteSpace(url)
        && width is > 0
        && height is > 0
        && width.Value / (double)height.Value >= 1.45;

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

public enum MediaTileBucket
{
    Other,
    Book,
    Comic,
    Audiobook,
    Movie,
    Tv,
    Music,
}
