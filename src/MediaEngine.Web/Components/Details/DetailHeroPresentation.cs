using MediaEngine.Contracts.Details;

namespace MediaEngine.Web.Components.Details;

public sealed class DetailHeroPresentation
{
    private DetailHeroPresentation(
        string heroClass,
        string gradientStyle,
        string eyebrow,
        bool useLogo,
        string? logoUrl,
        string title,
        string? subtitle,
        string? heroCopy,
        IReadOnlyList<MetadataPill> capabilityPills)
    {
        HeroClass = heroClass;
        GradientStyle = gradientStyle;
        Eyebrow = eyebrow;
        UseLogo = useLogo;
        LogoUrl = logoUrl;
        Title = title;
        Subtitle = subtitle;
        HeroCopy = heroCopy;
        CapabilityPills = capabilityPills;
    }

    public string HeroClass { get; }
    public string GradientStyle { get; }
    public string Eyebrow { get; }
    public bool UseLogo { get; }
    public string? LogoUrl { get; }
    public string Title { get; }
    public string? Subtitle { get; }
    public string? HeroCopy { get; }
    public IReadOnlyList<MetadataPill> CapabilityPills { get; }

    public static DetailHeroPresentation From(DetailPageViewModel model)
    {
        var mode = NormalizeMode(model.Artwork.HeroArtwork.Mode);
        var useLogo = mode == HeroArtworkMode.BackdropWithLogo && !string.IsNullOrWhiteSpace(model.Artwork.LogoUrl);
        var copy = model.EntityType == DetailEntityType.MusicAlbum
            ? null
            : !string.IsNullOrWhiteSpace(model.Tagline)
                ? model.Tagline
                : !string.IsNullOrWhiteSpace(model.Description)
                    ? Truncate(model.Description, 260)
                    : null;

        return new DetailHeroPresentation(
            $"tl-detail-hero--{ToKebabCase(mode.ToString())}",
            BuildGradientStyle(model.Artwork),
            FormatEntityType(model.EntityType),
            useLogo,
            useLogo ? model.Artwork.LogoUrl : null,
            model.Title,
            model.Subtitle,
            copy,
            BuildCapabilityPills(model));
    }

    private static HeroArtworkMode NormalizeMode(HeroArtworkMode mode) => mode switch
    {
#pragma warning disable CS0618
        HeroArtworkMode.Background => HeroArtworkMode.BackdropWithRenderedTitle,
        HeroArtworkMode.CoverFallback => HeroArtworkMode.ArtworkFallback,
#pragma warning restore CS0618
        _ => mode,
    };

    private static IReadOnlyList<MetadataPill> BuildCapabilityPills(DetailPageViewModel model)
        => model.Metadata
            .Where(item => IsCapabilityKind(item.Kind))
            .Where(item => !string.IsNullOrWhiteSpace(item.Label))
            .DistinctBy(item => $"{item.Kind}:{item.Label}", StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();

    private static bool IsCapabilityKind(string? kind)
        => string.Equals(kind, "sync", StringComparison.OrdinalIgnoreCase)
           || string.Equals(kind, "quality", StringComparison.OrdinalIgnoreCase);

    private static string BuildGradientStyle(ArtworkSet artwork)
    {
        var primary = artwork.PrimaryColor ?? "#C9922E";
        var secondary = artwork.SecondaryColor ?? "#271A3A";
        var accent = artwork.AccentColor ?? "#4F7DBA";
        var parsedColors = new[] { primary, secondary, accent }
            .Select(TryParseHexColor)
            .Where(color => color is not null)
            .Select(color => color!.Value)
            .ToList();
        var background = parsedColors.Count == 0
            ? (R: 8, G: 10, B: 15)
            : parsedColors.OrderBy(RelativeLuminance).First();
        var accentColor = parsedColors.Count == 0
            ? (R: 44, G: 180, B: 190)
            : parsedColors.OrderByDescending(Saturation).ThenByDescending(RelativeLuminance).First();
        var surface = Mix(background, (255, 255, 255), 0.08);
        var shadow = Mix(background, (0, 0, 0), 0.72);

        return string.Join(
            ';',
            $"--tl-detail-primary:{primary}",
            $"--tl-detail-secondary:{secondary}",
            $"--tl-detail-accent:{accent}",
            $"--hero-bg-rgb:{ToRgb(background)}",
            $"--hero-accent-rgb:{ToRgb(accentColor)}",
            $"--hero-shadow-rgb:{ToRgb(shadow)}",
            $"--hero-surface-rgb:{ToRgb(surface)}",
            "--hero-text-rgb:246, 240, 232") + ';';
    }

    private static string FormatEntityType(DetailEntityType entityType) => entityType switch
    {
        DetailEntityType.TvShow => "TV Show",
        DetailEntityType.TvSeason => "TV Season",
        DetailEntityType.TvEpisode => "TV Episode",
        DetailEntityType.MovieSeries => "Movie Series",
        DetailEntityType.BookSeries => "Book Series",
        DetailEntityType.ComicIssue => "Comic",
        DetailEntityType.ComicSeries => "Comics",
        DetailEntityType.MusicAlbum => "Album",
        DetailEntityType.MusicArtist => "Artist",
        DetailEntityType.MusicTrack => "Track",
        _ => entityType.ToString(),
    };

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max].TrimEnd() + "...";

    private static string ToKebabCase(string value)
    {
        var chars = new List<char>(value.Length + 4);
        foreach (var ch in value)
        {
            if (char.IsUpper(ch) && chars.Count > 0)
                chars.Add('-');

            chars.Add(char.ToLowerInvariant(ch));
        }

        return new string(chars.ToArray());
    }

    private static (int R, int G, int B)? TryParseHexColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var hex = value.Trim().TrimStart('#');
        if (hex.Length == 3)
            hex = string.Concat(hex.Select(ch => $"{ch}{ch}"));

        if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            return null;

        return ((rgb >> 16) & 0xff, (rgb >> 8) & 0xff, rgb & 0xff);
    }

    private static (int R, int G, int B) Mix((int R, int G, int B) a, (int R, int G, int B) b, double amount)
        => (
            (int)Math.Round((a.R * (1 - amount)) + (b.R * amount)),
            (int)Math.Round((a.G * (1 - amount)) + (b.G * amount)),
            (int)Math.Round((a.B * (1 - amount)) + (b.B * amount)));

    private static double RelativeLuminance((int R, int G, int B) color)
        => (0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B);

    private static double Saturation((int R, int G, int B) color)
    {
        var max = Math.Max(color.R, Math.Max(color.G, color.B)) / 255d;
        var min = Math.Min(color.R, Math.Min(color.G, color.B)) / 255d;
        return max == 0 ? 0 : (max - min) / max;
    }

    private static string ToRgb((int R, int G, int B) color) => $"{color.R}, {color.G}, {color.B}";
}
