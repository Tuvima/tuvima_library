using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Models;
using SkiaSharp;

namespace MediaEngine.Providers.Services;

public sealed class ArtworkPaletteService : IArtworkPaletteService
{
    private readonly ConcurrentDictionary<string, ArtworkPalette> _cache = new(StringComparer.Ordinal);

    public Task<ArtworkPalette> GeneratePaletteAsync(
        IReadOnlyList<ArtworkPaletteSource> sources,
        ArtworkPaletteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ArtworkPaletteOptions();
        var usableSources = sources
            .Where(source => source is not null)
            .Where(source => !string.IsNullOrWhiteSpace(source.LocalPath) || !string.IsNullOrWhiteSpace(source.ImageUrl))
            .OrderBy(source => StableSourceKey(source), StringComparer.Ordinal)
            .Take(Math.Clamp(options.MaxImagesToAnalyze, 1, 12))
            .ToList();

        if (usableSources.Count == 0)
        {
            return Task.FromResult(ArtworkPalette.TuvimaDefault(options.GenerateCssStrings));
        }

        var cacheKey = BuildCacheKey(usableSources, options);
        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            return Task.FromResult(cached);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var palette = ArtworkPaletteColorEngine.Generate(usableSources, options, cancellationToken);
        _cache[cacheKey] = palette;
        return Task.FromResult(palette);
    }

    private static string BuildCacheKey(IReadOnlyList<ArtworkPaletteSource> sources, ArtworkPaletteOptions options)
    {
        var input = string.Join(
            "|",
            sources.Select(source => string.Join(
                ":",
                StableSourceKey(source),
                source.LocalPath ?? "",
                source.ImageUrl ?? "")));
        input = $"{options.PreferDarkBackground}:{options.GenerateCssStrings}:{options.MaxImagesToAnalyze}:{options.StableSeed}:{input}";

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash);
    }

    private static string StableSourceKey(ArtworkPaletteSource source) =>
        !string.IsNullOrWhiteSpace(source.Id)
            ? source.Id
            : !string.IsNullOrWhiteSpace(source.LocalPath)
                ? source.LocalPath!
                : source.ImageUrl;
}

internal static class ArtworkPaletteColorEngine
{
    private const int WorkingLongEdge = 72;
    private static readonly Rgb FallbackBase = new(12, 10, 20);
    private static readonly Rgb FallbackBaseDark = new(7, 8, 18);
    private static readonly Rgb FallbackAccent = new(124, 92, 255);
    private static readonly Rgb FallbackWarmGlow = new(245, 158, 80);
    private static readonly Rgb FallbackCoolGlow = new(82, 184, 255);

    public static ArtworkPalette Generate(
        IReadOnlyList<ArtworkPaletteSource> sources,
        ArtworkPaletteOptions options,
        CancellationToken cancellationToken)
    {
        var imagePalettes = new List<ImagePalette>();
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var imagePalette = TryExtractImagePalette(source);
            if (imagePalette is not null)
            {
                imagePalettes.Add(imagePalette.Value);
            }
        }

        if (imagePalettes.Count == 0)
        {
            return ArtworkPalette.TuvimaDefault(options.GenerateCssStrings);
        }

        var candidates = MergeCandidates(imagePalettes);
        var usable = candidates
            .Where(candidate => !IsExtreme(candidate.Color) && !IsFlatGray(candidate.Color))
            .OrderByDescending(candidate => candidate.Score)
            .ToList();

        var fallbackBecauseMuddy = usable.Count == 0
            || usable.Take(4).All(candidate => Saturation(candidate.Color) < 0.24d)
            || WeightedSaturation(candidates.OrderByDescending(candidate => candidate.Weight).Take(6)) < 0.18d
            || WeightedChannelSpread(candidates.OrderByDescending(candidate => candidate.Weight).Take(6)) < 34d;
        var baseSource = fallbackBecauseMuddy
            ? FallbackBase
            : usable.OrderByDescending(candidate => candidate.Weight).ThenBy(candidate => Luminance(candidate.Color)).First().Color;
        var accentCandidate = usable
            .Where(candidate => ColorDistance(candidate.Color, baseSource) > 42d)
            .OrderByDescending(candidate => AccentScore(candidate.Color) * (1d + (candidate.ImageHits * 0.16d)))
            .Cast<ColorCandidate?>()
            .FirstOrDefault();
        var accentSource = accentCandidate?.Color ?? (fallbackBecauseMuddy ? FallbackAccent : baseSource);
        var secondaryCandidate = usable
            .Where(candidate => ColorDistance(candidate.Color, accentSource) > 56d && ColorDistance(candidate.Color, baseSource) > 36d)
            .OrderByDescending(candidate => GlowScore(candidate.Color) * (1d + (candidate.ImageHits * 0.12d)))
            .Cast<ColorCandidate?>()
            .FirstOrDefault();
        var secondarySource = secondaryCandidate?.Color ?? (fallbackBecauseMuddy ? FallbackCoolGlow : Mix(accentSource, FallbackCoolGlow, 0.42d));

        var baseColor = fallbackBecauseMuddy
            ? FallbackBase
            : options.PreferDarkBackground
            ? DarkenForBackground(baseSource)
            : EnsureWhiteTextContrast(baseSource);
        var baseColorDark = fallbackBecauseMuddy ? FallbackBaseDark : Mix(baseColor, Rgb.Black, 0.45d);
        var accent = NormalizeAccent(accentSource);
        var secondary = NormalizeAccent(secondarySource);
        var contrast = ContrastRatio(Rgb.White, baseColor);
        var darkSafe = contrast >= 7d && ContrastRatio(Rgb.White, baseColorDark) >= 9d;

        if (!darkSafe)
        {
            baseColor = FallbackBase;
            baseColorDark = FallbackBaseDark;
            contrast = ContrastRatio(Rgb.White, baseColor);
            darkSafe = true;
        }

        var textOverlayAlpha = contrast < 9d ? 0.82d : 0.74d;
        var palette = new ArtworkPalette
        {
            BaseColor = ToHex(baseColor),
            BaseColorDark = ToHex(baseColorDark),
            AccentColor = ToHex(accent),
            AccentColorMuted = ToRgba(Mix(accent, baseColor, 0.48d), 0.30d),
            GlowColor = ToRgba(accent, fallbackBecauseMuddy ? 0.24d : 0.30d),
            SecondaryGlowColor = ToRgba(secondary, fallbackBecauseMuddy ? 0.18d : 0.22d),
            TextOverlayColor = ToRgba(new Rgb(5, 8, 16), textOverlayAlpha),
            BorderColor = ToRgba(Mix(accent, Rgb.White, 0.38d), 0.18d),
            ShadowColor = ToRgba(Mix(baseColorDark, Rgb.Black, 0.68d), 0.50d),
            IsDarkSafe = darkSafe,
            ContrastScore = Math.Round(contrast, 2),
        };

        return WithCss(palette, options.GenerateCssStrings);
    }

    public static (string PrimaryHex, string SecondaryHex, string AccentHex) ExtractLegacyPalette(SKBitmap bitmap)
    {
        var imagePalette = ExtractImagePalette(bitmap, "legacy");
        if (imagePalette.Candidates.Count == 0)
        {
            return ("#5DCAA5", "#25352F", "#7C5CFF");
        }

        var candidates = MergeCandidates([imagePalette])
            .Where(candidate => !IsExtreme(candidate.Color) && !IsFlatGray(candidate.Color))
            .OrderByDescending(candidate => candidate.Score)
            .ToList();

        if (candidates.Count == 0)
        {
            return ("#5DCAA5", "#25352F", "#7C5CFF");
        }

        var primary = candidates.OrderByDescending(candidate => candidate.Weight).First().Color;
        var secondaryCandidate = candidates
            .Where(candidate => ColorDistance(candidate.Color, primary) > 42d)
            .Cast<ColorCandidate?>()
            .FirstOrDefault();
        var secondary = secondaryCandidate?.Color
            ?? Mix(primary, Rgb.Black, 0.35d);
        var accent = candidates
            .Where(candidate => ColorDistance(candidate.Color, primary) > 45d && ColorDistance(candidate.Color, secondary) > 38d)
            .OrderByDescending(candidate => AccentScore(candidate.Color))
            .Cast<ColorCandidate?>()
            .FirstOrDefault()?.Color ?? NormalizeAccent(secondary);

        return (ToHex(primary), ToHex(secondary), ToHex(accent));
    }

    private static ArtworkPalette WithCss(ArtworkPalette palette, bool generateCssStrings)
    {
        if (!generateCssStrings)
        {
            var variables = ArtworkPalette.BuildVariables(palette);
            return palette with
            {
                CssVariables = variables,
                CssVariableStyle = ArtworkPalette.BuildVariableStyle(variables),
            };
        }

        var gradient = string.Create(
            CultureInfo.InvariantCulture,
            $"linear-gradient(90deg, {ToRgba(ParseHex(palette.BaseColorDark), 0.96d)} 0%, {ToRgba(ParseHex(palette.BaseColor), 0.88d)} 38%, {palette.AccentColorMuted} 100%)");
        var glow = string.Create(
            CultureInfo.InvariantCulture,
            $"radial-gradient(circle at 72% 48%, {palette.GlowColor} 0%, {palette.SecondaryGlowColor} 38%, transparent 72%)");

        var completed = palette with
        {
            CssGradient = gradient,
            CssRadialGlow = glow,
        };
        var completedVariables = ArtworkPalette.BuildVariables(completed);
        return completed with
        {
            CssVariables = completedVariables,
            CssVariableStyle = ArtworkPalette.BuildVariableStyle(completedVariables),
        };
    }

    private static ImagePalette? TryExtractImagePalette(ArtworkPaletteSource source)
    {
        var path = source.LocalPath;
        if (string.IsNullOrWhiteSpace(path) && Uri.TryCreate(source.ImageUrl, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            path = uri.LocalPath;
        }

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var bitmap = SKBitmap.Decode(path);
            return bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0
                ? null
                : ExtractImagePalette(bitmap, string.IsNullOrWhiteSpace(source.Id) ? path : source.Id);
        }
        catch (Exception) when (!System.Diagnostics.Debugger.IsAttached)
        {
            return null;
        }
    }

    private static ImagePalette ExtractImagePalette(SKBitmap bitmap, string sourceId)
    {
        using var sample = ResizeForSampling(bitmap);
        var source = sample ?? bitmap;
        var buckets = new Dictionary<int, Bucket>();
        var totalPixels = 0;

        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var pixel = source.GetPixel(x, y);
                if (pixel.Alpha < 32)
                {
                    continue;
                }

                totalPixels++;
                var key = Quantize(pixel);
                buckets.TryGetValue(key, out var bucket);
                bucket.Count++;
                bucket.Red += pixel.Red;
                bucket.Green += pixel.Green;
                bucket.Blue += pixel.Blue;
                buckets[key] = bucket;
            }
        }

        if (totalPixels == 0)
        {
            return new ImagePalette(sourceId, []);
        }

        var candidates = buckets.Values
            .Select(bucket =>
            {
                var color = new Rgb(
                    (int)Math.Round(bucket.Red / (double)bucket.Count),
                    (int)Math.Round(bucket.Green / (double)bucket.Count),
                    (int)Math.Round(bucket.Blue / (double)bucket.Count));
                var frequency = bucket.Count / (double)totalPixels;
                var score = frequency * UsefulnessScore(color);
                return new ColorCandidate(color, frequency, score, 1);
            })
            .Where(candidate => candidate.Weight >= 0.002d)
            .OrderByDescending(candidate => candidate.Score)
            .Take(18)
            .ToList();

        return new ImagePalette(sourceId, candidates);
    }

    private static SKBitmap? ResizeForSampling(SKBitmap bitmap)
    {
        var longEdge = Math.Max(bitmap.Width, bitmap.Height);
        if (longEdge <= WorkingLongEdge)
        {
            return null;
        }

        var scale = WorkingLongEdge / (double)longEdge;
        var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
        var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
        return bitmap.Resize(
            new SKImageInfo(width, height, SKColorType.Bgra8888, bitmap.AlphaType),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
    }

    private static List<ColorCandidate> MergeCandidates(IReadOnlyList<ImagePalette> imagePalettes)
    {
        var clusters = new List<Cluster>();
        var imageWeight = 1d / Math.Max(1, imagePalettes.Count);

        foreach (var imagePalette in imagePalettes)
        {
            foreach (var candidate in imagePalette.Candidates)
            {
                var weighted = candidate with
                {
                    Weight = Math.Min(candidate.Weight, 0.34d) * imageWeight,
                    Score = candidate.Score * imageWeight,
                };
                var cluster = clusters.FirstOrDefault(existing => ColorDistance(existing.Color, weighted.Color) < 38d);
                if (cluster is null)
                {
                    clusters.Add(new Cluster(weighted.Color, weighted.Weight, weighted.Score, new HashSet<string>(StringComparer.Ordinal) { imagePalette.SourceId }));
                }
                else
                {
                    cluster.Add(weighted, imagePalette.SourceId);
                }
            }
        }

        return clusters
            .Select(cluster =>
            {
                var imageHits = cluster.SourceIds.Count;
                var agreementBoost = 1d + Math.Min(0.65d, (imageHits - 1) * 0.18d);
                return new ColorCandidate(cluster.Color, cluster.Weight, cluster.Score * agreementBoost, imageHits);
            })
            .OrderByDescending(candidate => candidate.Score)
            .ToList();
    }

    private static double UsefulnessScore(Rgb color)
    {
        var saturation = Saturation(color);
        var luminance = Luminance(color);
        var score = 1d;

        if (IsExtreme(color))
        {
            score *= 0.12d;
        }

        if (IsFlatGray(color))
        {
            score *= 0.18d;
        }

        if (IsSkinTone(color))
        {
            score *= 0.38d;
        }

        score *= 0.45d + Math.Min(0.95d, saturation * 1.35d);
        score *= luminance switch
        {
            < 0.05d => 0.28d,
            > 0.86d => 0.34d,
            _ => 1d,
        };

        if (saturation > 0.86d && luminance > 0.68d)
        {
            score *= 0.64d;
        }

        return score;
    }

    private static double WeightedSaturation(IEnumerable<ColorCandidate> candidates)
    {
        var totalWeight = 0d;
        var totalSaturation = 0d;
        foreach (var candidate in candidates)
        {
            totalWeight += candidate.Weight;
            totalSaturation += Saturation(candidate.Color) * candidate.Weight;
        }

        return totalWeight <= 0d ? 0d : totalSaturation / totalWeight;
    }

    private static double WeightedChannelSpread(IEnumerable<ColorCandidate> candidates)
    {
        var totalWeight = 0d;
        var totalSpread = 0d;
        foreach (var candidate in candidates)
        {
            var max = Math.Max(candidate.Color.Red, Math.Max(candidate.Color.Green, candidate.Color.Blue));
            var min = Math.Min(candidate.Color.Red, Math.Min(candidate.Color.Green, candidate.Color.Blue));
            totalWeight += candidate.Weight;
            totalSpread += (max - min) * candidate.Weight;
        }

        return totalWeight <= 0d ? 0d : totalSpread / totalWeight;
    }

    private static double AccentScore(Rgb color)
    {
        var saturation = Saturation(color);
        var luminance = Luminance(color);
        var chroma = Math.Clamp(saturation, 0d, 0.82d);
        var luminanceScore = 1d - Math.Abs(luminance - 0.48d);
        return (chroma * 1.4d) + luminanceScore + (IsSkinTone(color) ? -0.65d : 0d);
    }

    private static double GlowScore(Rgb color) =>
        AccentScore(color) + (Hue(color) is >= 185d and <= 285d ? 0.18d : 0d);

    private static Rgb DarkenForBackground(Rgb color)
    {
        var saturation = Saturation(color);
        var hue = Hue(color);
        var targetLum = Math.Clamp(Luminance(color) * 0.28d, 0.028d, 0.095d);
        return FromHsl(hue, Math.Clamp(saturation * 0.78d, 0.18d, 0.52d), targetLum);
    }

    private static Rgb EnsureWhiteTextContrast(Rgb color)
    {
        var result = color;
        var guard = 0;
        while (ContrastRatio(Rgb.White, result) < 7d && guard++ < 20)
        {
            result = Mix(result, Rgb.Black, 0.12d);
        }

        return result;
    }

    private static Rgb NormalizeAccent(Rgb color)
    {
        var hue = Hue(color);
        var saturation = Math.Clamp(Saturation(color), 0.32d, 0.74d);
        var luminance = Math.Clamp(Luminance(color), 0.34d, 0.62d);
        return FromHsl(hue, saturation, luminance);
    }

    private static bool IsExtreme(Rgb color)
    {
        var luminance = Luminance(color);
        return luminance < 0.018d || luminance > 0.92d;
    }

    private static bool IsFlatGray(Rgb color) =>
        Saturation(color) < 0.09d || (Math.Abs(color.Red - color.Green) < 9 && Math.Abs(color.Green - color.Blue) < 9);

    private static bool IsSkinTone(Rgb color)
    {
        var hue = Hue(color);
        var saturation = Saturation(color);
        var luminance = Luminance(color);
        return hue is >= 18d and <= 52d
            && saturation is >= 0.20d and <= 0.74d
            && luminance is >= 0.22d and <= 0.78d
            && color.Red > color.Blue
            && color.Green > color.Blue * 0.72d;
    }

    private static int Quantize(SKColor color)
    {
        var red = color.Red / 24;
        var green = color.Green / 24;
        var blue = color.Blue / 24;
        return (red << 16) | (green << 8) | blue;
    }

    private static double ColorDistance(Rgb left, Rgb right)
    {
        var red = left.Red - right.Red;
        var green = left.Green - right.Green;
        var blue = left.Blue - right.Blue;
        return Math.Sqrt((red * red) + (green * green) + (blue * blue));
    }

    private static double ContrastRatio(Rgb left, Rgb right)
    {
        var leftLum = RelativeLuminance(left) + 0.05d;
        var rightLum = RelativeLuminance(right) + 0.05d;
        return Math.Max(leftLum, rightLum) / Math.Min(leftLum, rightLum);
    }

    private static double RelativeLuminance(Rgb color)
    {
        static double channel(int value)
        {
            var normalized = value / 255d;
            return normalized <= 0.03928d
                ? normalized / 12.92d
                : Math.Pow((normalized + 0.055d) / 1.055d, 2.4d);
        }

        return (0.2126d * channel(color.Red)) + (0.7152d * channel(color.Green)) + (0.0722d * channel(color.Blue));
    }

    private static double Luminance(Rgb color)
    {
        var max = Math.Max(color.Red, Math.Max(color.Green, color.Blue)) / 255d;
        var min = Math.Min(color.Red, Math.Min(color.Green, color.Blue)) / 255d;
        return (max + min) / 2d;
    }

    private static double Saturation(Rgb color)
    {
        var max = Math.Max(color.Red, Math.Max(color.Green, color.Blue)) / 255d;
        var min = Math.Min(color.Red, Math.Min(color.Green, color.Blue)) / 255d;
        if (Math.Abs(max - min) < 0.0001d)
        {
            return 0d;
        }

        var luminance = (max + min) / 2d;
        return (max - min) / (1d - Math.Abs((2d * luminance) - 1d));
    }

    private static double Hue(Rgb color)
    {
        var red = color.Red / 255d;
        var green = color.Green / 255d;
        var blue = color.Blue / 255d;
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var delta = max - min;
        if (delta <= 0.0001d)
        {
            return 0d;
        }

        var hue = max == red
            ? 60d * (((green - blue) / delta) % 6d)
            : max == green
                ? 60d * (((blue - red) / delta) + 2d)
                : 60d * (((red - green) / delta) + 4d);
        return hue < 0d ? hue + 360d : hue;
    }

    private static Rgb FromHsl(double hue, double saturation, double luminance)
    {
        var chroma = (1d - Math.Abs((2d * luminance) - 1d)) * saturation;
        var x = chroma * (1d - Math.Abs(((hue / 60d) % 2d) - 1d));
        var m = luminance - (chroma / 2d);
        var (r1, g1, b1) = hue switch
        {
            < 60d => (chroma, x, 0d),
            < 120d => (x, chroma, 0d),
            < 180d => (0d, chroma, x),
            < 240d => (0d, x, chroma),
            < 300d => (x, 0d, chroma),
            _ => (chroma, 0d, x),
        };

        return new Rgb(
            ToByte((r1 + m) * 255d),
            ToByte((g1 + m) * 255d),
            ToByte((b1 + m) * 255d));
    }

    private static Rgb Mix(Rgb source, Rgb target, double amount)
    {
        var clamped = Math.Clamp(amount, 0d, 1d);
        return new Rgb(
            ToByte((source.Red * (1d - clamped)) + (target.Red * clamped)),
            ToByte((source.Green * (1d - clamped)) + (target.Green * clamped)),
            ToByte((source.Blue * (1d - clamped)) + (target.Blue * clamped)));
    }

    private static Rgb ParseHex(string hex)
    {
        var value = hex.Trim().TrimStart('#');
        if (value.Length == 3)
        {
            value = string.Concat(value.Select(character => $"{character}{character}"));
        }

        return value.Length == 6 && int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)
            ? new Rgb((rgb >> 16) & 0xff, (rgb >> 8) & 0xff, rgb & 0xff)
            : FallbackBase;
    }

    private static string ToHex(Rgb color) =>
        $"#{color.Red:x2}{color.Green:x2}{color.Blue:x2}";

    private static string ToRgba(Rgb color, double alpha) =>
        string.Create(CultureInfo.InvariantCulture, $"rgba({color.Red}, {color.Green}, {color.Blue}, {Math.Clamp(alpha, 0d, 1d):0.00})");

    private static int ToByte(double value) =>
        (int)Math.Round(Math.Clamp(value, 0d, 255d));

    private readonly record struct Rgb(int Red, int Green, int Blue)
    {
        public static readonly Rgb Black = new(0, 0, 0);
        public static readonly Rgb White = new(255, 255, 255);
    }

    private readonly record struct ColorCandidate(Rgb Color, double Weight, double Score, int ImageHits);

    private readonly record struct ImagePalette(string SourceId, IReadOnlyList<ColorCandidate> Candidates);

    private struct Bucket
    {
        public int Count;
        public long Red;
        public long Green;
        public long Blue;
    }

    private sealed class Cluster
    {
        public Cluster(Rgb color, double weight, double score, HashSet<string> sourceIds)
        {
            Color = color;
            Weight = weight;
            Score = score;
            SourceIds = sourceIds;
        }

        public Rgb Color { get; private set; }
        public double Weight { get; private set; }
        public double Score { get; private set; }
        public HashSet<string> SourceIds { get; }

        public void Add(ColorCandidate candidate, string sourceId)
        {
            var nextWeight = Weight + candidate.Weight;
            Color = nextWeight <= 0d
                ? Color
                : new Rgb(
                    ToByte(((Color.Red * Weight) + (candidate.Color.Red * candidate.Weight)) / nextWeight),
                    ToByte(((Color.Green * Weight) + (candidate.Color.Green * candidate.Weight)) / nextWeight),
                    ToByte(((Color.Blue * Weight) + (candidate.Color.Blue * candidate.Weight)) / nextWeight));
            Weight = nextWeight;
            Score += candidate.Score;
            SourceIds.Add(sourceId);
        }
    }
}
