using MediaEngine.Contracts.Display;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Integration;

namespace MediaEngine.Web.Services.Discovery;

public sealed class DiscoveryComposerService
{
    private readonly IEngineApiClient _api;

    public DiscoveryComposerService(IEngineApiClient api)
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
            .Select(shelf => new DiscoveryShelfViewModel
            {
                Title = shelf.Title,
                Subtitle = shelf.Subtitle,
                Items = shelf.Items.Select(FromDisplayCard).ToList(),
                SeeAllRoute = shelf.SeeAllRoute,
            })
            .ToList();

        return new DiscoveryPageViewModel
        {
            Key = page.Key,
            AccentColor = "#1CE783",
            Hero = page.Hero is null ? null : new DiscoveryHeroViewModel
            {
                Eyebrow = page.Hero.Eyebrow ?? "From your library",
                Title = page.Hero.Title,
                Subtitle = page.Hero.Subtitle,
                BackgroundImageUrl = page.Hero.Artwork.BackgroundUrl ?? page.Hero.Artwork.BannerUrl,
                HeroBackgroundImageUrl = page.Hero.Artwork.BackgroundUrl ?? page.Hero.Artwork.BannerUrl,
                BannerImageUrl = page.Hero.Artwork.BannerUrl,
                PreviewImageUrl = page.Hero.Artwork.CoverUrl ?? page.Hero.Artwork.SquareUrl,
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

    public static DiscoveryCardViewModel FromDisplayCard(DisplayCardDto card)
    {
        var bucket = GetBucket(card.MediaType);
        var shape = card.PreferredShape switch
        {
            "landscape" => DiscoveryCardShape.Landscape,
            "square" => DiscoveryCardShape.Square,
            _ => DiscoveryCardShape.Portrait,
        };

        var presentation = card.Presentation switch
        {
            "tvSeries" => DiscoveryCardPresentation.TvSeries,
            "movieSeries" => DiscoveryCardPresentation.MovieSeries,
            "bookSeries" => DiscoveryCardPresentation.BookSeries,
            "comicSeries" => DiscoveryCardPresentation.ComicSeries,
            "audiobookSeries" => DiscoveryCardPresentation.AudiobookSeries,
            "album" => DiscoveryCardPresentation.Album,
            "artist" => DiscoveryCardPresentation.Artist,
            _ => DiscoveryCardPresentation.Default,
        };

        var surface = DiscoveryArtworkResolver.Resolve(
            bucket,
            presentation,
            [
                new ArtworkVariant(ArtworkRole.Background, card.Artwork.BackgroundUrl, card.Artwork.BackgroundWidthPx, card.Artwork.BackgroundHeightPx),
                new ArtworkVariant(ArtworkRole.Banner, card.Artwork.BannerUrl, card.Artwork.BannerWidthPx, card.Artwork.BannerHeightPx),
                new ArtworkVariant(ArtworkRole.Square, card.Artwork.SquareUrl, card.Artwork.SquareWidthPx, card.Artwork.SquareHeightPx),
                new ArtworkVariant(ArtworkRole.Cover, card.Artwork.CoverUrl, card.Artwork.CoverWidthPx, card.Artwork.CoverHeightPx),
            ]);

        return new DiscoveryCardViewModel
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
            Shape = shape,
            Presentation = presentation,
            SurfaceKind = surface.SurfaceKind,
            HoverLayout = surface.HoverLayout,
            TileTextMode = string.Equals(card.TileTextMode, "coverOnly", StringComparison.OrdinalIgnoreCase)
                ? DiscoveryTileTextMode.CoverOnly
                : DiscoveryTileTextMode.Caption,
            PreviewPlacement = string.Equals(card.PreviewPlacement, "bottom", StringComparison.OrdinalIgnoreCase)
                ? DiscoveryPreviewPlacement.Bottom
                : DiscoveryPreviewPlacement.Smart,
            TileImageUrl = surface.TileImageUrl,
            HoverImageUrl = surface.HoverImageUrl,
            HeroBackgroundImageUrl = card.Artwork.BackgroundUrl,
            PreviewImageUrl = card.Artwork.CoverUrl ?? card.Artwork.SquareUrl,
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

    private static DiscoveryBucket GetBucket(string? mediaType)
    {
        var value = (mediaType ?? string.Empty).ToLowerInvariant();
        if (value.Contains("comic") || value.Contains("cbz") || value.Contains("cbr"))
        {
            return DiscoveryBucket.Comic;
        }

        if (value.Contains("audiobook") || value.Contains("m4b"))
        {
            return DiscoveryBucket.Audiobook;
        }

        if (value.Contains("music"))
        {
            return DiscoveryBucket.Music;
        }

        if (value.Contains("movie") || value.Contains("video"))
        {
            return DiscoveryBucket.Movie;
        }

        if (value.Contains("tv"))
        {
            return DiscoveryBucket.Tv;
        }

        if (value.Contains("book") || value.Contains("epub"))
        {
            return DiscoveryBucket.Book;
        }

        return DiscoveryBucket.Other;
    }

    private static string AccentForBucket(DiscoveryBucket bucket) => bucket switch
    {
        DiscoveryBucket.Book => "#5DCAA5",
        DiscoveryBucket.Comic => "#FB923C",
        DiscoveryBucket.Audiobook => "#84CC16",
        DiscoveryBucket.Movie => "#60A5FA",
        DiscoveryBucket.Tv => "#38BDF8",
        DiscoveryBucket.Music => "#1ED760",
        _ => "#C9922E",
    };
}

public enum DiscoveryBucket
{
    Other,
    Book,
    Comic,
    Audiobook,
    Movie,
    Tv,
    Music,
}
