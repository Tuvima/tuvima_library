using MediaEngine.Domain;
using MediaEngine.Web.Models.ViewDTOs;

namespace MediaEngine.Web.Services.Discovery;

public enum ArtworkRole
{
    Cover,
    Square,
    Background,
    Banner,
    Hero,
    ArtistPhoto,
}

public sealed record ArtworkVariant(
    ArtworkRole Role,
    string? Url,
    int? WidthPx = null,
    int? HeightPx = null,
    string? AspectClass = null)
{
    public bool HasUrl => !string.IsNullOrWhiteSpace(Url);

    public DiscoveryCardShape Shape => DiscoveryArtworkResolver.ShapeFor(this);
}

public sealed record DiscoverySurfaceSelection(
    DiscoverySurfaceKind SurfaceKind,
    DiscoveryHoverLayout HoverLayout,
    string? TileImageUrl,
    string? HoverImageUrl,
    string? HeroBackgroundImageUrl,
    string? PreviewImageUrl,
    DiscoveryImageFitMode TileImageFitMode,
    DiscoveryImageFitMode HoverImageFitMode,
    DiscoveryCardShape Shape);

public static class DiscoveryArtworkResolver
{
    public static DiscoverySurfaceSelection Resolve(
        DiscoveryBucket bucket,
        DiscoveryCardPresentation presentation,
        IReadOnlyList<ArtworkVariant> variants)
    {
        var selected = SelectVariant(bucket, presentation, variants);
        var shape = ShapeForSelected(bucket, presentation, selected);
        var surfaceKind = SurfaceFor(presentation, shape);
        var hoverLayout = surfaceKind == DiscoverySurfaceKind.BannerLandscape
            ? DiscoveryHoverLayout.BannerPopover
            : DiscoveryHoverLayout.ArtOnlyPopover;
        var fit = FitFor(bucket, selected, shape);

        return new DiscoverySurfaceSelection(
            surfaceKind,
            hoverLayout,
            TileImageUrl: selected?.Url,
            HoverImageUrl: selected?.Url,
            HeroBackgroundImageUrl: selected?.Url,
            PreviewImageUrl: selected?.Url,
            TileImageFitMode: fit,
            HoverImageFitMode: surfaceKind == DiscoverySurfaceKind.BannerLandscape ? DiscoveryImageFitMode.Fill : DiscoveryImageFitMode.Contain,
            Shape: shape);
    }

    public static DiscoveryCardShape ShapeFor(ArtworkVariant variant)
    {
        if (variant.WidthPx is > 0 && variant.HeightPx is > 0)
        {
            var ratio = (double)variant.WidthPx.Value / variant.HeightPx.Value;
            if (ratio >= 1.32)
                return DiscoveryCardShape.Landscape;
            if (ratio >= 0.86)
                return DiscoveryCardShape.Square;
            return DiscoveryCardShape.Portrait;
        }

        if (string.Equals(variant.AspectClass, ArtworkAspectClasses.LandscapeWide, StringComparison.OrdinalIgnoreCase)
            || string.Equals(variant.AspectClass, ArtworkAspectClasses.BannerStrip, StringComparison.OrdinalIgnoreCase))
        {
            return DiscoveryCardShape.Landscape;
        }

        if (string.Equals(variant.AspectClass, ArtworkAspectClasses.Square, StringComparison.OrdinalIgnoreCase))
        {
            return DiscoveryCardShape.Square;
        }

        return variant.Role switch
        {
            ArtworkRole.Background or ArtworkRole.Banner or ArtworkRole.Hero => DiscoveryCardShape.Landscape,
            ArtworkRole.Square or ArtworkRole.ArtistPhoto => DiscoveryCardShape.Square,
            _ => DiscoveryCardShape.Portrait,
        };
    }

    private static ArtworkVariant? SelectVariant(
        DiscoveryBucket bucket,
        DiscoveryCardPresentation presentation,
        IReadOnlyList<ArtworkVariant> variants)
    {
        if (presentation == DiscoveryCardPresentation.Artist)
        {
            return First(variants, ArtworkRole.ArtistPhoto);
        }

        return bucket switch
        {
            DiscoveryBucket.Movie or DiscoveryBucket.Tv =>
                First(variants, ArtworkRole.Background, ArtworkRole.Banner, ArtworkRole.Hero)
                ?? First(variants, ArtworkRole.Cover),
            DiscoveryBucket.Music =>
                First(variants, ArtworkRole.Square, ArtworkRole.Cover),
            DiscoveryBucket.Audiobook =>
                First(variants, ArtworkRole.Square, ArtworkRole.Cover),
            DiscoveryBucket.Book or DiscoveryBucket.Comic =>
                First(variants, ArtworkRole.Cover, ArtworkRole.Square),
            _ => First(variants, ArtworkRole.Cover, ArtworkRole.Square, ArtworkRole.Background, ArtworkRole.Banner, ArtworkRole.ArtistPhoto),
        };
    }

    private static DiscoveryCardShape ShapeForSelected(
        DiscoveryBucket bucket,
        DiscoveryCardPresentation presentation,
        ArtworkVariant? selected)
    {
        if (presentation == DiscoveryCardPresentation.Artist)
            return DiscoveryCardShape.Square;

        if (bucket is DiscoveryBucket.Music or DiscoveryBucket.Audiobook)
            return DiscoveryCardShape.Square;

        return selected?.Shape ?? DefaultShape(bucket);
    }

    private static DiscoveryCardShape DefaultShape(DiscoveryBucket bucket) => bucket switch
    {
        DiscoveryBucket.Movie or DiscoveryBucket.Tv => DiscoveryCardShape.Portrait,
        DiscoveryBucket.Music or DiscoveryBucket.Audiobook => DiscoveryCardShape.Square,
        _ => DiscoveryCardShape.Portrait,
    };

    private static DiscoverySurfaceKind SurfaceFor(DiscoveryCardPresentation presentation, DiscoveryCardShape shape)
    {
        if (presentation == DiscoveryCardPresentation.Artist)
            return DiscoverySurfaceKind.ArtistPhotoSquare;

        return shape switch
        {
            DiscoveryCardShape.Landscape => DiscoverySurfaceKind.BannerLandscape,
            DiscoveryCardShape.Square => DiscoverySurfaceKind.CoverSquare,
            _ => DiscoverySurfaceKind.CoverPortrait,
        };
    }

    private static DiscoveryImageFitMode FitFor(DiscoveryBucket bucket, ArtworkVariant? selected, DiscoveryCardShape shape)
    {
        if (selected is null)
            return DiscoveryImageFitMode.Fill;

        if (shape == DiscoveryCardShape.Square
            && bucket == DiscoveryBucket.Audiobook
            && selected.Role == ArtworkRole.Cover
            && selected.Shape == DiscoveryCardShape.Portrait)
        {
            return DiscoveryImageFitMode.Contain;
        }

        return DiscoveryImageFitMode.Fill;
    }

    private static ArtworkVariant? First(IReadOnlyList<ArtworkVariant> variants, params ArtworkRole[] roles)
    {
        foreach (var role in roles)
        {
            var match = variants.FirstOrDefault(variant => variant.Role == role && variant.HasUrl);
            if (match is not null)
                return match;
        }

        return null;
    }
}
