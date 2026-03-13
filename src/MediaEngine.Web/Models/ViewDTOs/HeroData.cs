using MudBlazor;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Unified data shape for the hero banner component.
/// Normalizes both <see cref="HubViewModel"/> and <see cref="JourneyItemViewModel"/>
/// into a single rendering contract.
/// </summary>
public sealed record HeroData
{
    // ── Identity ────────────────────────────────────────────────────────────
    public Guid?   EntityId       { get; init; }
    public string  Title          { get; init; } = string.Empty;
    public string? Subtitle       { get; init; }
    public string? Author         { get; init; }
    public string? Narrator       { get; init; }
    public string? Description    { get; init; }

    // ── Imagery ─────────────────────────────────────────────────────────────
    public string? CoverUrl       { get; init; }
    public string? HeroUrl        { get; init; }
    public string? DominantColor  { get; init; }

    // ── Metadata badges ─────────────────────────────────────────────────────
    public string? Year           { get; init; }
    public string? MediaTypeLabel { get; init; }
    public string? Genre          { get; init; }
    public IReadOnlyList<string> Genres   { get; init; } = [];
    public IReadOnlyList<string> GenreQids { get; init; } = [];
    public string? Rating         { get; init; }
    public string? Series         { get; init; }
    public string? SeriesPosition { get; init; }
    public int?    WorkCount      { get; init; }

    // ── CTA ─────────────────────────────────────────────────────────────────
    public string? ActionLabel    { get; init; }
    public string? ActionIcon     { get; init; }
    public double? ProgressPct    { get; init; }

    // ── Layout hint ─────────────────────────────────────────────────────────

    /// <summary>
    /// True for Books, Comics, Audiobooks (portrait cover art).
    /// False for Movies, TV, Music, Podcasts (landscape backdrop).
    /// </summary>
    public bool IsPortraitMedia => FormatLabel(MediaTypeLabel) switch
    {
        "Book" or "Audiobook" or "Comic" => true,
        _ => false,
    };

    // ── Factory methods ─────────────────────────────────────────────────────

    /// <summary>Create hero data from a Hub (lane pages, non-journey carousel slides).</summary>
    public static HeroData FromHub(HubViewModel hub) => new()
    {
        EntityId       = hub.Id,
        Title          = hub.DisplayName,
        Author         = hub.Author,
        Description    = Truncate(hub.Description, 200),
        CoverUrl       = hub.CoverUrl,
        HeroUrl        = hub.HeroUrl,
        DominantColor  = hub.DominantHexColor,
        Year           = hub.Year,
        MediaTypeLabel = FormatLabel(hub.PrimaryMediaType),
        Genre          = hub.Genre,
        Genres         = hub.Genres,
        GenreQids      = hub.GenreQids,
        Rating         = hub.Rating,
        Series         = hub.Series,
        WorkCount      = hub.WorkCount > 1 ? hub.WorkCount : null,
    };

    /// <summary>Create hero data from a journey item (carousel slides with progress).</summary>
    public static HeroData FromJourney(JourneyItemViewModel item) => new()
    {
        EntityId       = item.HubId,
        Title          = item.Title,
        Subtitle       = item.HubDisplayName,
        Author         = item.Author,
        Narrator       = item.Narrator != item.Author ? item.Narrator : null,
        Description    = Truncate(item.Description, 200),
        CoverUrl       = item.CoverUrl,
        HeroUrl        = item.HeroUrl,
        MediaTypeLabel = item.FormatMediaType,
        Series         = item.Series,
        SeriesPosition = item.SeriesPosition,
        ActionLabel    = item.ActionLabel,
        ActionIcon     = IconForMediaType(item.MediaType),
        ProgressPct    = item.ProgressPct,
    };

    /// <summary>
    /// Create hero data from a Hub with an active journey overlay
    /// (lane pages where the hero hub has progress).
    /// </summary>
    public static HeroData FromHubWithJourney(HubViewModel hub, JourneyItemViewModel journey) => new()
    {
        EntityId       = hub.Id,
        Title          = hub.DisplayName,
        // Subtitle is set by the caller via PhraseTemplateService
        Author         = hub.Author,
        Description    = Truncate(hub.Description, 200),
        CoverUrl       = hub.CoverUrl,
        HeroUrl        = hub.HeroUrl,
        DominantColor  = hub.DominantHexColor,
        Year           = hub.Year,
        MediaTypeLabel = FormatLabel(hub.PrimaryMediaType),
        Genre          = hub.Genre,
        Genres         = hub.Genres,
        GenreQids      = hub.GenreQids,
        Rating         = hub.Rating,
        Series         = hub.Series,
        WorkCount      = hub.WorkCount > 1 ? hub.WorkCount : null,
        ActionLabel    = journey.ActionLabel,
        ActionIcon     = IconForMediaType(journey.MediaType),
        ProgressPct    = journey.ProgressPct,
    };

    // ── Shared helpers ──────────────────────────────────────────────────────

    internal static string FormatLabel(string? mediaType)
    {
        if (string.IsNullOrEmpty(mediaType)) return "";
        var t = mediaType.ToLowerInvariant();
        if (t.Contains("epub") || t.Contains("book"))  return "Book";
        if (t.Contains("audio") || t.Contains("m4b"))  return "Audiobook";
        if (t.Contains("video") || t.Contains("movie")) return "Movie";
        if (t.Contains("comic") || t.Contains("cbz"))   return "Comic";
        if (t.Contains("mkv") || t.Contains("mp4"))     return "Video";
        return mediaType;
    }

    internal static string IconForMediaType(string? mediaType)
    {
        if (string.IsNullOrEmpty(mediaType)) return Icons.Material.Filled.MenuBook;
        var t = mediaType.ToLowerInvariant();
        if (t.Contains("audio") || t.Contains("m4b"))   return Icons.Material.Filled.Headphones;
        if (t.Contains("video") || t.Contains("movie")) return Icons.Material.Filled.PlayArrow;
        if (t.Contains("comic") || t.Contains("cbz"))   return Icons.Material.Filled.AutoStories;
        if (t.Contains("music"))                         return Icons.Material.Filled.MusicNote;
        if (t.Contains("podcast"))                       return Icons.Material.Filled.Podcasts;
        if (t.Contains("tv"))                            return Icons.Material.Filled.Tv;
        return Icons.Material.Filled.MenuBook;
    }

    /// <summary>Colour for media type badges/indicators. Reusable by PosterCard and hero.</summary>
    internal static string FormatBadgeColor(string? mediaType)
    {
        var label = FormatLabel(mediaType);
        return label switch
        {
            "Book"      => "rgba(37, 99, 235, 0.85)",   // blue
            "Audiobook" => "rgba(139, 92, 246, 0.85)",  // purple
            "Movie"     => "rgba(0, 191, 165, 0.85)",   // teal
            "Video"     => "rgba(0, 191, 165, 0.85)",   // teal
            "Comic"     => "rgba(124, 77, 255, 0.85)",  // violet
            _           => "rgba(37, 99, 235, 0.85)",   // blue default
        };
    }

    internal static string? Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return null;
        return text.Length <= maxLength ? text : text[..maxLength].TrimEnd() + "\u2026";
    }
}
