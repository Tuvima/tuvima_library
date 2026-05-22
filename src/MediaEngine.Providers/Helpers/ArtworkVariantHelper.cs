using MediaEngine.Domain;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Services;
using MediaEngine.Providers.Services;
using SkiaSharp;

namespace MediaEngine.Providers.Helpers;

/// <summary>
/// Measures artwork, extracts palette hexes, and prepares UI renditions.
/// </summary>
public static class ArtworkVariantHelper
{
    private const int SmallLongEdge = 320;
    private const int MediumLongEdge = 960;
    private const int LargeLongEdge = 2160;

    public static void StampMetadataAndRenditions(EntityAsset asset, AssetPathService assetPathService)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(assetPathService);

        if (string.IsNullOrWhiteSpace(asset.LocalImagePath) || !File.Exists(asset.LocalImagePath))
        {
            return;
        }

        using var bitmap = SKBitmap.Decode(asset.LocalImagePath);
        if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0)
        {
            return;
        }

        asset.WidthPx = bitmap.Width;
        asset.HeightPx = bitmap.Height;
        asset.AspectClass = ClassifyAspect(bitmap.Width, bitmap.Height);

        var palette = ArtworkPaletteColorEngine.ExtractLegacyPalette(bitmap);
        asset.PrimaryHex = palette.PrimaryHex;
        asset.SecondaryHex = palette.SecondaryHex;
        asset.AccentHex = palette.AccentHex;

        if (!ShouldGenerateRenditions(asset.AssetTypeValue))
        {
            asset.LocalImagePathSmall = null;
            asset.LocalImagePathMedium = null;
            asset.LocalImagePathLarge = null;
            return;
        }

        var extension = NormalizeExtension(asset.LocalImagePath);
        var longEdge = Math.Max(bitmap.Width, bitmap.Height);

        asset.LocalImagePathSmall = BuildRenditionPath(assetPathService, asset, "s", extension);
        asset.LocalImagePathMedium = BuildRenditionPath(assetPathService, asset, "m", extension);
        asset.LocalImagePathLarge = BuildRenditionPath(assetPathService, asset, "l", extension);

        WriteRendition(bitmap, asset.LocalImagePathSmall, extension, SmallLongEdge, longEdge);
        WriteRendition(bitmap, asset.LocalImagePathMedium, extension, MediumLongEdge, longEdge);
        WriteRendition(bitmap, asset.LocalImagePathLarge, extension, LargeLongEdge, longEdge);
    }

    public static bool ShouldGenerateRenditions(string assetTypeValue) => assetTypeValue switch
    {
        "Logo" or "ClearArt" or "DiscArt" => false,
        _ => true,
    };

    public static string ClassifyAspect(int widthPx, int heightPx)
    {
        if (widthPx <= 0 || heightPx <= 0)
        {
            return ArtworkAspectClasses.UnsupportedRect;
        }

        var ratio = widthPx / (double)heightPx;
        if (ratio < 0.82d)
        {
            return ArtworkAspectClasses.Portrait;
        }

        if (ratio <= 1.18d)
        {
            return ArtworkAspectClasses.Square;
        }

        if (ratio >= 1.50d && ratio <= 2.40d)
        {
            return ArtworkAspectClasses.LandscapeWide;
        }

        if (ratio > 2.40d)
        {
            return ArtworkAspectClasses.BannerStrip;
        }

        return ArtworkAspectClasses.UnsupportedRect;
    }

    private static string BuildRenditionPath(
        AssetPathService assetPathService,
        EntityAsset asset,
        string sizeKey,
        string extension) =>
        assetPathService.GetCentralDerivedPath(
            asset.EntityType,
            asset.EntityId,
            $"renditions-{asset.AssetTypeValue}-{asset.Id:N}",
            $"{sizeKey}{extension}");

    private static void WriteRendition(
        SKBitmap sourceBitmap,
        string outputPath,
        string extension,
        int targetLongEdge,
        int sourceLongEdge)
    {
        AssetPathService.EnsureDirectory(outputPath);

        if (sourceLongEdge <= targetLongEdge)
        {
            using var originalImage = SKImage.FromBitmap(sourceBitmap);
            using var originalData = EncodeImage(originalImage, extension);
            using var output = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            originalData.SaveTo(output);
            return;
        }

        var scale = targetLongEdge / (double)sourceLongEdge;
        var width = Math.Max(1, (int)Math.Round(sourceBitmap.Width * scale));
        var height = Math.Max(1, (int)Math.Round(sourceBitmap.Height * scale));
        using var resized = sourceBitmap.Resize(
            new SKImageInfo(width, height, sourceBitmap.ColorType, sourceBitmap.AlphaType),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        if (resized is null)
        {
            return;
        }

        using var image = SKImage.FromBitmap(resized);
        using var data = EncodeImage(image, extension);
        using var resizedOutput = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        data.SaveTo(resizedOutput);
    }

    private static SKData EncodeImage(SKImage image, string extension) =>
        string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
            ? image.Encode(SKEncodedImageFormat.Png, 100)
            : image.Encode(SKEncodedImageFormat.Jpeg, 88);

    private static string NormalizeExtension(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
            ? ".png"
            : ".jpg";
    }

}
