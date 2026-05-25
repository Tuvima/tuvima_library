using MediaEngine.Domain;
using MediaEngine.Web.Models.ViewDTOs;

namespace MediaEngine.Web.Services.MediaTiles;

public enum ArtworkRole
{
    Cover,
    Square,
    Background,
    Banner,
    Hero,
    ArtistPhoto,
}

public sealed record MediaTileArtworkVariant(
    ArtworkRole Role,
    string? SmallUrl = null,
    string? MediumUrl = null,
    string? LargeUrl = null,
    int? WidthPx = null,
    int? HeightPx = null,
    string? AspectClass = null)
{
    public bool HasUrl => !string.IsNullOrWhiteSpace(SmallUrl)
        || !string.IsNullOrWhiteSpace(MediumUrl)
        || !string.IsNullOrWhiteSpace(LargeUrl);

    public string? TileUrl => FirstNonBlank(SmallUrl, MediumUrl);

    public string? HoverUrl => FirstNonBlank(MediumUrl, LargeUrl);

    public string? HeroUrl => FirstNonBlank(LargeUrl, MediumUrl);

    public MediaTileShape Shape => MediaTileArtworkResolver.ShapeFor(this);

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

public sealed record MediaTileSurfaceSelection(
    MediaTileSurfaceKind SurfaceKind,
    MediaTileHoverLayout HoverLayout,
    string? TileImageUrl,
    string? TileImageSrcSet,
    string? HoverImageUrl,
    string? HoverImageSrcSet,
    string? HeroBackgroundImageUrl,
    string? PreviewImageUrl,
    MediaTileImageFitMode TileImageFitMode,
    MediaTileImageFitMode HoverImageFitMode,
    MediaTileShape Shape);

public static class MediaTileArtworkResolver
{
    public static MediaTileSurfaceSelection Resolve(
        MediaTileBucket bucket,
        MediaTilePresentation presentation,
        IReadOnlyList<MediaTileArtworkVariant> variants)
    {
        var selected = SelectVariant(bucket, presentation, variants);
        var shape = ShapeForSelected(bucket, presentation, selected);
        var surfaceKind = SurfaceFor(presentation, shape);
        var hoverLayout = surfaceKind == MediaTileSurfaceKind.BannerLandscape
            ? MediaTileHoverLayout.BannerPopover
            : MediaTileHoverLayout.ArtOnlyPopover;
        var fit = FitFor(bucket, selected, shape);

        return new MediaTileSurfaceSelection(
            surfaceKind,
            hoverLayout,
            TileImageUrl: selected?.TileUrl,
            TileImageSrcSet: BuildSrcSet((selected?.SmallUrl, 320), (selected?.MediumUrl, 960)),
            HoverImageUrl: selected?.HoverUrl,
            HoverImageSrcSet: BuildSrcSet((selected?.MediumUrl, 960), (selected?.LargeUrl, 2160)),
            HeroBackgroundImageUrl: selected?.HeroUrl,
            PreviewImageUrl: selected?.HoverUrl,
            TileImageFitMode: fit,
            HoverImageFitMode: surfaceKind == MediaTileSurfaceKind.BannerLandscape ? MediaTileImageFitMode.Fill : MediaTileImageFitMode.Contain,
            Shape: shape);
    }

    public static MediaTileShape ShapeFor(MediaTileArtworkVariant variant)
    {
        if (variant.WidthPx is > 0 && variant.HeightPx is > 0)
        {
            var ratio = (double)variant.WidthPx.Value / variant.HeightPx.Value;
            if (ratio >= 1.32)
                return MediaTileShape.Landscape;
            if (ratio >= 0.86)
                return MediaTileShape.Square;
            return MediaTileShape.Portrait;
        }

        if (string.Equals(variant.AspectClass, ArtworkAspectClasses.LandscapeWide, StringComparison.OrdinalIgnoreCase)
            || string.Equals(variant.AspectClass, ArtworkAspectClasses.BannerStrip, StringComparison.OrdinalIgnoreCase))
        {
            return MediaTileShape.Landscape;
        }

        if (string.Equals(variant.AspectClass, ArtworkAspectClasses.Square, StringComparison.OrdinalIgnoreCase))
        {
            return MediaTileShape.Square;
        }

        return variant.Role switch
        {
            ArtworkRole.Background or ArtworkRole.Banner or ArtworkRole.Hero => MediaTileShape.Landscape,
            ArtworkRole.Square or ArtworkRole.ArtistPhoto => MediaTileShape.Square,
            _ => MediaTileShape.Portrait,
        };
    }

    private static MediaTileArtworkVariant? SelectVariant(
        MediaTileBucket bucket,
        MediaTilePresentation presentation,
        IReadOnlyList<MediaTileArtworkVariant> variants)
    {
        if (presentation == MediaTilePresentation.Artist)
        {
            return First(variants, ArtworkRole.ArtistPhoto);
        }

        return bucket switch
        {
            MediaTileBucket.Movie or MediaTileBucket.Tv =>
                First(variants, ArtworkRole.Background, ArtworkRole.Banner, ArtworkRole.Hero)
                ?? First(variants, ArtworkRole.Cover),
            MediaTileBucket.Music =>
                First(variants, ArtworkRole.Square, ArtworkRole.Cover),
            MediaTileBucket.Audiobook =>
                First(variants, ArtworkRole.Square, ArtworkRole.Cover),
            MediaTileBucket.Book or MediaTileBucket.Comic =>
                First(variants, ArtworkRole.Cover, ArtworkRole.Square),
            _ => First(variants, ArtworkRole.Cover, ArtworkRole.Square, ArtworkRole.Background, ArtworkRole.Banner, ArtworkRole.ArtistPhoto),
        };
    }

    private static MediaTileShape ShapeForSelected(
        MediaTileBucket bucket,
        MediaTilePresentation presentation,
        MediaTileArtworkVariant? selected)
    {
        if (presentation == MediaTilePresentation.Artist)
            return MediaTileShape.Square;

        if (bucket is MediaTileBucket.Music or MediaTileBucket.Audiobook)
            return MediaTileShape.Square;

        return selected?.Shape ?? DefaultShape(bucket);
    }

    private static MediaTileShape DefaultShape(MediaTileBucket bucket) => bucket switch
    {
        MediaTileBucket.Movie or MediaTileBucket.Tv => MediaTileShape.Portrait,
        MediaTileBucket.Music or MediaTileBucket.Audiobook => MediaTileShape.Square,
        _ => MediaTileShape.Portrait,
    };

    private static MediaTileSurfaceKind SurfaceFor(MediaTilePresentation presentation, MediaTileShape shape)
    {
        if (presentation == MediaTilePresentation.Artist)
            return MediaTileSurfaceKind.ArtistPhotoSquare;

        return shape switch
        {
            MediaTileShape.Landscape => MediaTileSurfaceKind.BannerLandscape,
            MediaTileShape.Square => MediaTileSurfaceKind.CoverSquare,
            _ => MediaTileSurfaceKind.CoverPortrait,
        };
    }

    private static MediaTileImageFitMode FitFor(MediaTileBucket bucket, MediaTileArtworkVariant? selected, MediaTileShape shape)
    {
        if (selected is null)
            return MediaTileImageFitMode.Fill;

        if (shape == MediaTileShape.Square
            && bucket == MediaTileBucket.Audiobook
            && selected.Role == ArtworkRole.Cover
            && selected.Shape == MediaTileShape.Portrait)
        {
            return MediaTileImageFitMode.Contain;
        }

        return MediaTileImageFitMode.Fill;
    }

    private static MediaTileArtworkVariant? First(IReadOnlyList<MediaTileArtworkVariant> variants, params ArtworkRole[] roles)
    {
        foreach (var role in roles)
        {
            var match = variants.FirstOrDefault(variant => variant.Role == role && variant.HasUrl);
            if (match is not null)
                return match;
        }

        return null;
    }

    private static string? BuildSrcSet(params (string? Url, int Width)[] candidates)
    {
        var parts = candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Url))
            .DistinctBy(candidate => candidate.Url, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => $"{candidate.Url} {candidate.Width}w")
            .ToList();

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }
}
