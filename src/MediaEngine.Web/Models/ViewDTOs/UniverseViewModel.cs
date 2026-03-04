namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Flattened, cross-media-type view of the entire user library.
/// Collapses all Hubs and their Works into a single canonical list
/// suitable for rendering the Universe grid, stats bar, and hero tiles.
///
/// Produced by <see cref="MediaEngine.Web.Services.Integration.UniverseMapper"/>
/// from a set of <see cref="HubViewModel"/>s.
/// </summary>
public sealed class UniverseViewModel
{
    public string                   Title            { get; init; } = "My Library";

    /// <summary>
    /// Hex colour of the most-represented media bucket in this library
    /// (e.g. "#FF8F00" if books dominate, "#00BFA5" for video, "#7C4DFF" for comics).
    /// </summary>
    public string                   DominantHexColor { get; init; } = "#7C4DFF";

    public List<MediaItemViewModel> Items            { get; init; } = [];

    // ── Computed aggregates ───────────────────────────────────────────────────

    public int TotalCount  => Items.Count;
    public int BookCount   => Items.Count(i => i.MediaTypeBucket == MediaTypeBucket.Book);
    public int VideoCount  => Items.Count(i => i.MediaTypeBucket == MediaTypeBucket.Video);
    public int ComicCount  => Items.Count(i => i.MediaTypeBucket == MediaTypeBucket.Comic);
    public int AudioCount  => Items.Count(i => i.MediaTypeBucket == MediaTypeBucket.Audio);
}

/// <summary>
/// A single media item normalised from any <see cref="WorkViewModel"/>
/// regardless of its original media type (Book / Video / Comic / Unknown).
/// Each item carries its own brand colour so the UI can render heterogeneous
/// lists without per-type switch statements in razor markup.
/// </summary>
public sealed class MediaItemViewModel
{
    public Guid            Id               { get; init; }
    public Guid            HubId            { get; init; }

    /// <summary>Raw media-type string from the domain enum (e.g. "Epub", "Video", "Cbz").</summary>
    public string          MediaType        { get; init; } = string.Empty;

    public string          Title            { get; init; } = string.Empty;
    public string?         Author           { get; init; }
    public string?         Year             { get; init; }

    /// <summary>
    /// Per-item brand colour derived from <see cref="MediaTypeBucket"/>.
    /// Use this for card accents, progress indicators, and icon tints.
    /// </summary>
    public string          DominantHexColor { get; init; } = "#7C4DFF";

    public MediaTypeBucket MediaTypeBucket  { get; init; }
}

/// <summary>
/// Broad media-type category used for icon selection and colour assignment.
/// Maps the fine-grained domain <c>MediaType</c> enum into three UI buckets.
/// </summary>
public enum MediaTypeBucket
{
    Book,
    Video,
    Comic,
    Audio,
    Unknown,
}
