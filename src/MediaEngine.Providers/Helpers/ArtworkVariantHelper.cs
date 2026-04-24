using MediaEngine.Domain;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Services;
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

        var palette = ExtractPalette(bitmap);
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

    private static ArtworkPalette ExtractPalette(SKBitmap bitmap)
    {
        using var sample = bitmap.Resize(
            new SKImageInfo(
                Math.Max(8, Math.Min(48, bitmap.Width)),
                Math.Max(8, Math.Min(48, bitmap.Height)),
                SKColorType.Bgra8888,
                bitmap.AlphaType),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));

        var source = sample ?? bitmap;
        var buckets = new Dictionary<int, int>();

        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var color = source.GetPixel(x, y);
                if (color.Alpha < 24)
                {
                    continue;
                }

                var packed = QuantizeColor(color);
                buckets[packed] = buckets.TryGetValue(packed, out var count) ? count + 1 : 1;
            }
        }

        var colors = buckets
            .OrderByDescending(pair => pair.Value)
            .Select(pair => ToColor(pair.Key))
            .ToList();

        var primary = colors.FirstOrDefault(IsUsablePaletteColor);
        if (primary == default)
        {
            primary = colors.FirstOrDefault();
        }

        var secondary = colors.FirstOrDefault(color => IsDistinct(primary, color) && IsUsablePaletteColor(color));
        if (secondary == default)
        {
            secondary = colors.FirstOrDefault(color => IsDistinct(primary, color));
        }

        var accent = colors
            .OrderByDescending(GetAccentScore)
            .FirstOrDefault(color => IsDistinct(primary, color) && IsDistinct(secondary, color));
        if (accent == default)
        {
            accent = secondary != default ? secondary : primary;
        }

        var primaryHex = ToHex(primary == default ? new SKColor(93, 202, 165) : primary);
        var secondaryHex = ToHex(secondary == default ? Blend(primary, SKColors.Black, 0.35f) : secondary);
        var accentHex = ToHex(accent == default ? Blend(primary, SKColors.White, 0.2f) : accent);

        return new ArtworkPalette(primaryHex, secondaryHex, accentHex);
    }

    private static int QuantizeColor(SKColor color)
    {
        var red = color.Red / 32;
        var green = color.Green / 32;
        var blue = color.Blue / 32;
        return (red << 16) | (green << 8) | blue;
    }

    private static SKColor ToColor(int packed)
    {
        var red = ((packed >> 16) & 0xFF) * 32;
        var green = ((packed >> 8) & 0xFF) * 32;
        var blue = (packed & 0xFF) * 32;
        return new SKColor((byte)Math.Min(red, 255), (byte)Math.Min(green, 255), (byte)Math.Min(blue, 255));
    }

    private static bool IsUsablePaletteColor(SKColor color)
    {
        var brightness = (0.299 * color.Red) + (0.587 * color.Green) + (0.114 * color.Blue);
        return brightness is > 18 and < 244;
    }

    private static bool IsDistinct(SKColor left, SKColor right)
    {
        if (left == default || right == default)
        {
            return false;
        }

        var red = left.Red - right.Red;
        var green = left.Green - right.Green;
        var blue = left.Blue - right.Blue;
        return (red * red) + (green * green) + (blue * blue) > 1800;
    }

    private static double GetAccentScore(SKColor color)
    {
        var max = Math.Max(color.Red, Math.Max(color.Green, color.Blue));
        var min = Math.Min(color.Red, Math.Min(color.Green, color.Blue));
        var saturation = max == 0 ? 0d : (max - min) / (double)max;
        var brightness = (0.299 * color.Red) + (0.587 * color.Green) + (0.114 * color.Blue);
        return saturation * 1000d + brightness;
    }

    private static SKColor Blend(SKColor source, SKColor target, float amount)
    {
        var clamped = Math.Clamp(amount, 0f, 1f);
        return new SKColor(
            (byte)Math.Round((source.Red * (1 - clamped)) + (target.Red * clamped)),
            (byte)Math.Round((source.Green * (1 - clamped)) + (target.Green * clamped)),
            (byte)Math.Round((source.Blue * (1 - clamped)) + (target.Blue * clamped)));
    }

    private static string ToHex(SKColor color) =>
        $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";

    private sealed record ArtworkPalette(string PrimaryHex, string SecondaryHex, string AccentHex);
}
