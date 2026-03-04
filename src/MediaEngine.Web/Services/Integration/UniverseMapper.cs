using MediaEngine.Web.Models.ViewDTOs;

namespace MediaEngine.Web.Services.Integration;

/// <summary>
/// Pure static utility that maps a list of <see cref="HubViewModel"/>s
/// (the raw HTTP API representation) into a <see cref="UniverseViewModel"/> —
/// the flat, cross-type view consumed by the Universe grid, stats bar,
/// and hero tiles.
///
/// Design notes:
/// <list type="bullet">
///   <item>No async I/O; call site decides when to refresh from the API.</item>
///   <item>All type-to-bucket classification lives here; components are colour-blind.</item>
///   <item>The dominant colour is the brand colour of the most-represented media bucket.</item>
/// </list>
/// </summary>
public static class UniverseMapper
{
    // ── Colour palette ────────────────────────────────────────────────────────
    // Chosen to complement the ThemeService palette:
    //   Book    = amber  #FF8F00  — warm, literary feel
    //   Video   = teal   #00BFA5  — matches secondary colour
    //   Comic   = violet #7C4DFF  — matches primary colour
    //   Audio   = rose   #EC407A  — distinct, vibrant
    //   Unknown = slate  #9E9E9E  — neutral fallback
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly IReadOnlyDictionary<MediaTypeBucket, string> BucketColours =
        new Dictionary<MediaTypeBucket, string>
        {
            [MediaTypeBucket.Book]    = "#FF8F00",
            [MediaTypeBucket.Video]   = "#00BFA5",
            [MediaTypeBucket.Comic]   = "#7C4DFF",
            [MediaTypeBucket.Audio]   = "#EC407A",
            [MediaTypeBucket.Unknown] = "#9E9E9E",
        };

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Flattens all Works from every Hub into a single <see cref="UniverseViewModel"/>.
    /// Empty hub list returns an empty library view (no exception).
    /// </summary>
    public static UniverseViewModel MapFromHubs(IReadOnlyList<HubViewModel> hubs)
    {
        var items = hubs
            .SelectMany(hub => hub.Works.Select(MapItem))
            .ToList();

        return new UniverseViewModel
        {
            Title            = DeriveTitle(hubs),
            DominantHexColor = DominantColour(items),
            Items            = items,
        };
    }

    /// <summary>
    /// Maps a single <see cref="WorkViewModel"/> to a <see cref="MediaItemViewModel"/>.
    /// Exposed publicly so callers can incrementally update a cached universe
    /// when a <c>MediaAdded</c> event arrives without a full hub refresh.
    /// </summary>
    public static MediaItemViewModel MapItem(WorkViewModel work)
    {
        var bucket = ClassifyBucket(work.MediaType);
        return new MediaItemViewModel
        {
            Id               = work.Id,
            HubId            = work.HubId,
            MediaType        = work.MediaType,
            Title            = work.Title,
            Author           = work.Author,
            Year             = work.Year,
            DominantHexColor = BucketColours[bucket],
            MediaTypeBucket  = bucket,
        };
    }

    /// <summary>Returns the brand hex colour for a given <see cref="MediaTypeBucket"/>.</summary>
    public static string ColourFor(MediaTypeBucket bucket) => BucketColours[bucket];

    /// <summary>
    /// Public entry-point for bucket classification — used by components that need
    /// to map a raw media-type string to an icon or colour (e.g. CommandPalette).
    /// </summary>
    public static MediaTypeBucket ClassifyBucketPublic(string mediaType) =>
        ClassifyBucket(mediaType);

    /// <summary>
    /// Returns the brand hex colour that best represents the dominant media type
    /// across a Hub's work list.  Used by <see cref="HubViewModel.DominantHexColor"/>
    /// to colour the Hub's bento tile without a full universe map pass.
    /// </summary>
    public static string ColourForHub(IEnumerable<WorkViewModel> works)
    {
        var buckets = works.Select(w => ClassifyBucket(w.MediaType)).ToList();

        if (buckets.Count == 0)
            return BucketColours[MediaTypeBucket.Unknown];

        var top = buckets
            .GroupBy(b => b)
            .OrderByDescending(g => g.Count())
            .First()
            .Key;

        return BucketColours[top];
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Classifies the raw domain media-type string (e.g. "Epub", "Video", "Cbz")
    /// into a broad UI bucket.  Case-insensitive substring matching keeps this
    /// resilient to future enum additions or casing changes in the API.
    /// </summary>
    private static MediaTypeBucket ClassifyBucket(string mediaType)
    {
        var t = mediaType.ToLowerInvariant();
        if (t.Contains("epub")  || t.Contains("book"))                       return MediaTypeBucket.Book;
        if (t.Contains("video") || t.Contains("movie") || t.Contains("mkv")
                                || t.Contains("mp4")   || t.Contains("avi")) return MediaTypeBucket.Video;
        if (t.Contains("comic") || t.Contains("cbz")   || t.Contains("cbr")) return MediaTypeBucket.Comic;
        if (t.Contains("audio") || t.Contains("mp3")   || t.Contains("flac")
                                || t.Contains("aac")   || t.Contains("m4a")
                                || t.Contains("ogg")   || t.Contains("wav")) return MediaTypeBucket.Audio;
        return MediaTypeBucket.Unknown;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the raw media-type string maps to the
    /// <see cref="MediaTypeBucket.Audio"/> bucket.  Used by Automotive Mode to
    /// filter the grid to audio-only content.
    /// </summary>
    public static bool IsAudio(string mediaType) =>
        ClassifyBucket(mediaType) == MediaTypeBucket.Audio;

    /// <summary>
    /// Derives a display title for the universe from its hub composition.
    /// Single-hub libraries use the hub's name; multi-hub libraries use "My Library".
    /// </summary>
    private static string DeriveTitle(IReadOnlyList<HubViewModel> hubs) =>
        hubs.Count switch
        {
            0 => "Empty Library",
            1 => hubs[0].DisplayName,
            _ => "My Library",
        };

    /// <summary>
    /// Returns the colour of the most-represented media bucket,
    /// defaulting to primary violet for an empty library.
    /// </summary>
    private static string DominantColour(List<MediaItemViewModel> items)
    {
        if (items.Count == 0)
            return BucketColours[MediaTypeBucket.Unknown];

        var topBucket = items
            .GroupBy(i => i.MediaTypeBucket)
            .OrderByDescending(g => g.Count())
            .First()
            .Key;

        return BucketColours[topBucket];
    }
}
