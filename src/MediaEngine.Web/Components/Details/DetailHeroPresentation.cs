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
        bool heroCopyHasMore,
        ProgressViewModel? progress,
        bool isWatchHero,
        bool usePrimaryHeroChrome,
        bool showTitleBar,
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
        HeroCopyHasMore = heroCopyHasMore;
        Progress = progress;
        IsWatchHero = isWatchHero;
        UsePrimaryHeroChrome = usePrimaryHeroChrome;
        ShowTitleBar = showTitleBar;
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
    public bool HeroCopyHasMore { get; }
    public ProgressViewModel? Progress { get; }
    public bool IsWatchHero { get; }
    public bool UsePrimaryHeroChrome { get; }
    public bool ShowTitleBar { get; }
    public IReadOnlyList<MetadataPill> CapabilityPills { get; }

    public static DetailHeroPresentation From(DetailPageViewModel model)
    {
        var mode = model.Artwork.HeroArtwork.Mode;
        var isWatchHero = IsWatchEntity(model.EntityType);
        var usePrimaryHeroChrome = isWatchHero || UsesPrimaryHeroChrome(model.EntityType);
        var useLogo = mode == HeroArtworkMode.BackdropWithLogo && !string.IsNullOrWhiteSpace(model.Artwork.LogoUrl);
        var copySource = model.EntityType switch
        {
            DetailEntityType.TvShow => model.Tagline,
            DetailEntityType.TvEpisode => FirstNonBlank(model.Description, model.Tagline),
            DetailEntityType.Movie => FirstNonBlank(model.Tagline, model.Description),
            _ when isWatchHero => FirstNonBlank(model.Tagline, model.Description),
            _ => FirstNonBlank(model.Tagline, model.Description),
        };
        var usesReadOverviewCopy = UsesReadOverviewCopy(model.EntityType);
        var readFirstParagraph = usesReadOverviewCopy
            ? FirstParagraph(copySource)
            : copySource;
        var copyLimit = isWatchHero ? 360 : 320;
        var copy = !string.IsNullOrWhiteSpace(readFirstParagraph)
            ? usesReadOverviewCopy
                ? readFirstParagraph
                : Truncate(readFirstParagraph, copyLimit)
            : null;
        var copyHasMore = usesReadOverviewCopy
            && !string.IsNullOrWhiteSpace(copySource)
            && !string.Equals(copySource.Trim(), readFirstParagraph?.Trim(), StringComparison.Ordinal);

        return new DetailHeroPresentation(
            BuildHeroClass(mode, model.EntityType, isWatchHero),
            BuildGradientStyle(model.Artwork),
            usePrimaryHeroChrome ? string.Empty : FormatEntityType(model.EntityType),
            useLogo,
            useLogo ? model.Artwork.LogoUrl : null,
            model.Title,
            ResolveSubtitle(model, isWatchHero),
            copy,
            copyHasMore,
            model.Progress,
            isWatchHero,
            usePrimaryHeroChrome,
            mode is not HeroArtworkMode.ArtworkFallback,
            BuildCapabilityPills(model, usePrimaryHeroChrome));
    }

    private static string BuildHeroClass(HeroArtworkMode mode, DetailEntityType entityType, bool isWatchHero)
    {
        var modeClass = mode switch
        {
            HeroArtworkMode.BackdropWithLogo => "tl-detail-hero--backdrop tl-detail-hero--backdrop-logo tl-detail-hero--backdrop-with-logo",
            HeroArtworkMode.BackdropWithRenderedTitle => "tl-detail-hero--backdrop tl-detail-hero--backdrop-title tl-detail-hero--backdrop-with-rendered-title",
            HeroArtworkMode.ArtworkFallback => "tl-detail-hero--artwork-fallback tl-detail-hero--cover-fallback",
            _ => "tl-detail-hero--placeholder",
        };

        if (isWatchHero)
            return $"{modeClass} tl-detail-hero--watch";

        if (entityType == DetailEntityType.Person)
            return $"{modeClass} tl-detail-hero--person";

        var surfaceClass = entityType switch
        {
            DetailEntityType.Book or DetailEntityType.ComicIssue or DetailEntityType.Work => "tl-detail-hero--read",
            DetailEntityType.Audiobook => "tl-detail-hero--listen",
            DetailEntityType.MusicAlbum or DetailEntityType.MusicArtist or DetailEntityType.MusicTrack => "tl-detail-hero--music",
            _ => "tl-detail-hero--fallback-surface",
        };

        var fallbackClass = mode == HeroArtworkMode.ArtworkFallback
            ? " tl-detail-hero--fallback-generated"
            : string.Empty;

        return $"{modeClass} {surfaceClass}{fallbackClass}";
    }

    private static string? ResolveSubtitle(DetailPageViewModel model, bool isWatchHero)
    {
        if (isWatchHero || UsesPrimaryHeroChrome(model.EntityType))
            return null;

        return model.Subtitle;
    }

    private static IReadOnlyList<MetadataPill> BuildCapabilityPills(DetailPageViewModel model, bool usePrimaryHeroChrome)
        => usePrimaryHeroChrome
            ? []
            : model.Metadata
                .Where(item => IsCapabilityKind(item.Kind))
                .Where(item => !string.IsNullOrWhiteSpace(item.Label))
                .DistinctBy(item => $"{item.Kind}:{item.Label}", StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .ToList();

    private static bool IsCapabilityKind(string? kind)
        => string.Equals(kind, "sync", StringComparison.OrdinalIgnoreCase)
           || string.Equals(kind, "quality", StringComparison.OrdinalIgnoreCase);

    private static bool UsesPrimaryHeroChrome(DetailEntityType entityType)
        => entityType is DetailEntityType.Book
            or DetailEntityType.ComicIssue
            or DetailEntityType.Audiobook
            or DetailEntityType.Work
            or DetailEntityType.MusicAlbum
            or DetailEntityType.MusicArtist
            or DetailEntityType.MusicTrack;

    private static bool UsesReadOverviewCopy(DetailEntityType entityType)
        => entityType is DetailEntityType.Book
            or DetailEntityType.BookSeries
            or DetailEntityType.ComicIssue
            or DetailEntityType.ComicSeries
            or DetailEntityType.Work;

    private static string? FirstParagraph(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        var paragraphEnd = normalized.IndexOf("\n\n", StringComparison.Ordinal);
        return (paragraphEnd >= 0 ? normalized[..paragraphEnd] : normalized)
            .Replace('\n', ' ')
            .Trim();
    }

    private static bool IsWatchEntity(DetailEntityType entityType)
        => entityType is DetailEntityType.Movie or DetailEntityType.TvShow or DetailEntityType.TvSeason or DetailEntityType.TvEpisode;

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string BuildGradientStyle(ArtworkSet artwork)
    {
        var primary = artwork.PrimaryColor ?? "#DCA53E";
        var secondary = artwork.SecondaryColor ?? "#0E1218";
        var accent = artwork.AccentColor ?? "#DCA53E";
        var backdropColors = new[] { artwork.BackdropLeftTopColor, artwork.BackdropLeftMiddleColor, artwork.BackdropLeftBottomColor };
        var paletteInputs = artwork.HeroArtwork.Mode is HeroArtworkMode.BackdropWithLogo or HeroArtworkMode.BackdropWithRenderedTitle
            && backdropColors.Any(color => !string.IsNullOrWhiteSpace(color))
            ? backdropColors
            : new[] { artwork.PrimaryColor, artwork.SecondaryColor, artwork.AccentColor };
        var parsedColors = paletteInputs
            .Select(TryParseHexColor)
            .Where(color => color is not null)
            .Select(color => color!.Value)
            .ToList();
        var hasPalette = parsedColors.Count > 0 && paletteInputs.Any(color => !IsKnownFallbackColor(color));
        var background = hasPalette
            ? BuildArtworkBackground(parsedColors)
            : (R: 8, G: 12, B: 18);
        var accentColor = hasPalette
            ? parsedColors.OrderByDescending(Saturation).ThenByDescending(RelativeLuminance).First()
            : (R: 220, G: 165, B: 62);
        var surface = hasPalette
            ? Mix(background, (255, 255, 255), 0.08)
            : (R: 14, G: 18, B: 24);
        var shadow = hasPalette
            ? Mix(background, (0, 0, 0), 0.72)
            : (R: 4, G: 7, B: 12);

        return string.Join(
            ';',
            $"--tl-detail-primary:{primary}",
            $"--tl-detail-secondary:{secondary}",
            $"--tl-detail-accent:{accent}",
            $"--hero-bg-rgb:{ToRgb(background)}",
            $"--hero-accent-rgb:{ToRgb(accentColor)}",
            $"--hero-shadow-rgb:{ToRgb(shadow)}",
            $"--hero-surface-rgb:{ToRgb(surface)}",
            $"--hero-backdrop-top-rgb:{ToRgb(ParseOrDefault(artwork.BackdropLeftTopColor, background))}",
            $"--hero-backdrop-middle-rgb:{ToRgb(ParseOrDefault(artwork.BackdropLeftMiddleColor, background))}",
            $"--hero-backdrop-bottom-rgb:{ToRgb(ParseOrDefault(artwork.BackdropLeftBottomColor, background))}",
            "--hero-text-rgb:245, 247, 250") + ';';
    }

    private static (int R, int G, int B) ParseOrDefault(string? value, (int R, int G, int B) fallback)
        => TryParseHexColor(value) ?? fallback;

    private static (int R, int G, int B) BuildArtworkBackground(IReadOnlyList<(int R, int G, int B)> colors)
    {
        // Prefer a representative chromatic color over pure black so the copy surface
        // feels like an extension of the artwork while remaining dark enough for text.
        var source = colors
            .Where(color => RelativeLuminance(color) >= 10)
            .OrderByDescending(color => Saturation(color) * 0.7 + Math.Min(RelativeLuminance(color), 150) / 255d * 0.3)
            .FirstOrDefault();

        if (source == default)
            source = colors.OrderByDescending(RelativeLuminance).First();

        var luminance = RelativeLuminance(source);
        if (luminance > 72)
            source = Mix(source, (0, 0, 0), 1 - (72 / luminance));
        else if (luminance < 26)
            source = Mix(source, (255, 255, 255), Math.Min(0.18, (26 - luminance) / 110));

        return source;
    }

    private static string FormatEntityType(DetailEntityType entityType) => entityType switch
    {
        DetailEntityType.TvShow => "TV Show",
        DetailEntityType.TvSeason => "TV Season",
        DetailEntityType.TvEpisode => "TV Episode",
        DetailEntityType.MovieSeries => "Movie Series",
        DetailEntityType.BookSeries => "Book Series",
        DetailEntityType.ComicIssue => "Comic",
        DetailEntityType.ComicSeries => "Comic Volume",
        DetailEntityType.MusicAlbum => "Album",
        DetailEntityType.MusicArtist => "Artist",
        DetailEntityType.MusicTrack => "Track",
        _ => entityType.ToString(),
    };

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max].TrimEnd() + "...";

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

    private static bool IsKnownFallbackColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        var normalized = value.Trim().ToUpperInvariant();
        return normalized is "#C9922E" or "#271A3A" or "#4F7DBA";
    }
}
