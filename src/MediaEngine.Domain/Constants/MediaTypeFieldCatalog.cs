using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Constants;

/// <summary>
/// Single source of truth for which metadata fields are relevant per media type,
/// and how search results should be displayed for each media type.
/// Used by the hydration pipeline (field extraction), scoring engine (field validation),
/// and Dashboard search UI (display layout and field picker).
/// </summary>
public static class MediaTypeFieldCatalog
{
    /// <summary>
    /// Returns the complete list of metadata field keys relevant for a given media type.
    /// Providers should extract all of these fields when available — the Priority Cascade
    /// determines which values survive. Be greedy: collect everything, let Wikidata sort it out.
    /// </summary>
    public static IReadOnlyList<string> GetFields(MediaType type) => type switch
    {
        MediaType.Books => BookFields,
        MediaType.Audiobooks => AudiobookFields,
        MediaType.Music => MusicFields,
        MediaType.Movies => MovieFields,
        MediaType.TV => TvFields,
        MediaType.Comics => ComicFields,
        _ => DefaultFields,
    };

    /// <summary>
    /// Returns the search result display configuration for a given media type.
    /// Drives the SearchResultCard component layout in metadata resolution overlays.
    /// </summary>
    public static SearchDisplayConfig GetSearchDisplay(MediaType type) => type switch
    {
        MediaType.Books => BookSearchDisplay,
        MediaType.Audiobooks => AudiobookSearchDisplay,
        MediaType.Music => MusicSearchDisplay,
        MediaType.Movies => MovieSearchDisplay,
        MediaType.TV => TvSearchDisplay,
        MediaType.Comics => ComicSearchDisplay,
        _ => DefaultSearchDisplay,
    };

    /// <summary>
    /// Returns the Wikidata search result display configuration.
    /// Wikidata results have a fixed layout regardless of media type.
    /// </summary>
    public static SearchDisplayConfig GetWikidataSearchDisplay() => WikidataSearchDisplay;

    /// <summary>
    /// Returns the list of fields available for the search field picker in the
    /// Metadata resolution overlays. These are the fields a user can search by
    /// when manually matching a retail provider.
    /// </summary>
    public static IReadOnlyList<SearchableField> GetSearchableFields(MediaType type) => type switch
    {
        MediaType.Books => BookSearchableFields,
        MediaType.Audiobooks => AudiobookSearchableFields,
        MediaType.Music => MusicSearchableFields,
        MediaType.Movies => MovieSearchableFields,
        MediaType.TV => TvSearchableFields,
        MediaType.Comics => ComicSearchableFields,
        _ => DefaultSearchableFields,
    };

    // ── Field lists per media type ───────────────────────────────────────────

    private static readonly string[] BookFields =
    [
        MetadataFieldConstants.Title, MetadataFieldConstants.Author,
        MetadataFieldConstants.Year, MetadataFieldConstants.Description,
        MetadataFieldConstants.Cover, BridgeIdKeys.Isbn,
        MetadataFieldConstants.Series, MetadataFieldConstants.SeriesPosition,
        MetadataFieldConstants.PublisherField, MetadataFieldConstants.PageCount,
        MetadataFieldConstants.Genre, MetadataFieldConstants.Language,
        MetadataFieldConstants.Rating, MetadataFieldConstants.OriginalTitle,
    ];

    private static readonly string[] AudiobookFields =
    [
        MetadataFieldConstants.Title, MetadataFieldConstants.Author,
        MetadataFieldConstants.Narrator, MetadataFieldConstants.Year,
        MetadataFieldConstants.Description, MetadataFieldConstants.Cover,
        BridgeIdKeys.Isbn, BridgeIdKeys.Asin,
        MetadataFieldConstants.Series, MetadataFieldConstants.SeriesPosition,
        MetadataFieldConstants.DurationField, MetadataFieldConstants.PublisherField,
        MetadataFieldConstants.Genre, MetadataFieldConstants.Language,
        MetadataFieldConstants.Rating, MetadataFieldConstants.OriginalTitle,
    ];

    private static readonly string[] MusicFields =
    [
        MetadataFieldConstants.Title, MetadataFieldConstants.Artist,
        MetadataFieldConstants.Album, MetadataFieldConstants.TrackNumber,
        MetadataFieldConstants.Year, MetadataFieldConstants.Genre,
        MetadataFieldConstants.Cover, MetadataFieldConstants.DurationField,
        MetadataFieldConstants.Composer, BridgeIdKeys.MusicBrainzId,
        MetadataFieldConstants.Language, MetadataFieldConstants.Rating,
    ];

    private static readonly string[] MovieFields =
    [
        MetadataFieldConstants.Title, MetadataFieldConstants.Director,
        MetadataFieldConstants.CastMember, MetadataFieldConstants.Screenwriter,
        MetadataFieldConstants.Composer, MetadataFieldConstants.Year,
        MetadataFieldConstants.Description, MetadataFieldConstants.Cover,
        MetadataFieldConstants.Genre, MetadataFieldConstants.Runtime,
        MetadataFieldConstants.Rating, BridgeIdKeys.TmdbId, BridgeIdKeys.ImdbId,
        MetadataFieldConstants.Language, MetadataFieldConstants.OriginalTitle,
    ];

    private static readonly string[] TvFields =
    [
        MetadataFieldConstants.Title, MetadataFieldConstants.ShowName,
        MetadataFieldConstants.SeasonNumber, MetadataFieldConstants.EpisodeNumber,
        MetadataFieldConstants.EpisodeTitle, MetadataFieldConstants.Director,
        MetadataFieldConstants.CastMember, MetadataFieldConstants.Screenwriter,
        MetadataFieldConstants.Year, MetadataFieldConstants.Description,
        MetadataFieldConstants.Cover, MetadataFieldConstants.Genre,
        MetadataFieldConstants.Runtime, MetadataFieldConstants.Rating,
        BridgeIdKeys.TmdbId, BridgeIdKeys.ImdbId, MetadataFieldConstants.Language,
    ];

    private static readonly string[] ComicFields =
    [
        MetadataFieldConstants.Title, MetadataFieldConstants.Author,
        MetadataFieldConstants.Illustrator, MetadataFieldConstants.Year,
        MetadataFieldConstants.Description, MetadataFieldConstants.Cover,
        MetadataFieldConstants.Series, MetadataFieldConstants.SeriesPosition,
        MetadataFieldConstants.PublisherField, MetadataFieldConstants.Genre,
        BridgeIdKeys.Isbn, BridgeIdKeys.ComicVineId,
    ];

    private static readonly string[] DefaultFields =
    [
        MetadataFieldConstants.Title, MetadataFieldConstants.Author,
        MetadataFieldConstants.Year, MetadataFieldConstants.Description,
        MetadataFieldConstants.Cover,
    ];

    // ── Search display configs ───────────────────────────────────────────────

    private static readonly SearchDisplayConfig BookSearchDisplay = new(
        Headline: MetadataFieldConstants.Title,
        Subline: MetadataFieldConstants.Author,
        Tertiary: MetadataFieldConstants.Year,
        DetailFields: [MetadataFieldConstants.PublisherField, MetadataFieldConstants.PageCount, MetadataFieldConstants.Language],
        BridgeLabels: [BridgeIdKeys.Isbn, BridgeIdKeys.AppleBooksId, BridgeIdKeys.OpenLibraryId]
    );

    private static readonly SearchDisplayConfig AudiobookSearchDisplay = new(
        Headline: MetadataFieldConstants.Title,
        Subline: MetadataFieldConstants.Author,
        Tertiary: MetadataFieldConstants.Narrator,
        DetailFields: [MetadataFieldConstants.Year, MetadataFieldConstants.DurationField, MetadataFieldConstants.PublisherField],
        BridgeLabels: [BridgeIdKeys.Isbn, BridgeIdKeys.Asin, BridgeIdKeys.MusicBrainzId, BridgeIdKeys.AppleBooksId]
    );

    private static readonly SearchDisplayConfig MusicSearchDisplay = new(
        Headline: MetadataFieldConstants.Title,
        Subline: MetadataFieldConstants.Artist,
        Tertiary: MetadataFieldConstants.Album,
        DetailFields: [MetadataFieldConstants.Year, MetadataFieldConstants.DurationField, MetadataFieldConstants.TrackNumber, MetadataFieldConstants.Genre, MetadataFieldConstants.Composer],
        BridgeLabels: [BridgeIdKeys.AppleMusicId, BridgeIdKeys.AppleMusicCollectionId, BridgeIdKeys.MusicBrainzId]
    );

    private static readonly SearchDisplayConfig MovieSearchDisplay = new(
        Headline: MetadataFieldConstants.Title,
        Subline: MetadataFieldConstants.Director,
        Tertiary: MetadataFieldConstants.Year,
        DetailFields: [MetadataFieldConstants.Runtime, MetadataFieldConstants.Genre, MetadataFieldConstants.Language, MetadataFieldConstants.Rating],
        BridgeLabels: [BridgeIdKeys.TmdbId, BridgeIdKeys.ImdbId]
    );

    private static readonly SearchDisplayConfig TvSearchDisplay = new(
        Headline: MetadataFieldConstants.ShowName,
        Subline: MetadataFieldConstants.EpisodeTitle,
        Tertiary: MetadataFieldConstants.Year,
        DetailFields: [MetadataFieldConstants.SeasonNumber, MetadataFieldConstants.EpisodeNumber, MetadataFieldConstants.Runtime],
        BridgeLabels: [BridgeIdKeys.TmdbId, BridgeIdKeys.ImdbId]
    );

    private static readonly SearchDisplayConfig ComicSearchDisplay = new(
        Headline: MetadataFieldConstants.Title,
        Subline: MetadataFieldConstants.Author,
        Tertiary: MetadataFieldConstants.Year,
        DetailFields: [MetadataFieldConstants.PublisherField, MetadataFieldConstants.SeriesPosition, MetadataFieldConstants.Series],
        BridgeLabels: [BridgeIdKeys.Isbn, BridgeIdKeys.ComicVineId]
    );

    private static readonly SearchDisplayConfig DefaultSearchDisplay = new(
        Headline: MetadataFieldConstants.Title,
        Subline: MetadataFieldConstants.Author,
        Tertiary: MetadataFieldConstants.Year,
        DetailFields: [],
        BridgeLabels: []
    );

    private static readonly SearchDisplayConfig WikidataSearchDisplay = new(
        Headline: "label",
        Subline: "description",
        Tertiary: "instance_of",
        DetailFields: [MetadataFieldConstants.Author, MetadataFieldConstants.Year, MetadataFieldConstants.Genre, MetadataFieldConstants.Series],
        BridgeLabels: [BridgeIdKeys.WikidataQid, BridgeIdKeys.Isbn, BridgeIdKeys.TmdbId, BridgeIdKeys.ImdbId, BridgeIdKeys.MusicBrainzId]
    );

    // ── Searchable fields per media type ─────────────────────────────────────

    private static readonly SearchableField[] BookSearchableFields =
    [
        new(MetadataFieldConstants.Title, "Title", IsDefault: true),
        new(MetadataFieldConstants.Author, "Author"),
        new(BridgeIdKeys.Isbn, "ISBN"),
        new(MetadataFieldConstants.PublisherField, "Publisher"),
    ];

    private static readonly SearchableField[] AudiobookSearchableFields =
    [
        new(MetadataFieldConstants.Title, "Title", IsDefault: true),
        new(MetadataFieldConstants.Author, "Author"),
        new(MetadataFieldConstants.Narrator, "Narrator"),
        new(BridgeIdKeys.Isbn, "ISBN"),
        new(BridgeIdKeys.Asin, "ASIN"),
    ];

    private static readonly SearchableField[] MusicSearchableFields =
    [
        new(MetadataFieldConstants.Artist, "Artist", IsDefault: true),
        new(MetadataFieldConstants.Album, "Album"),
        new(MetadataFieldConstants.Title, "Title"),
        new(MetadataFieldConstants.TrackNumber, "Track #"),
        new(MetadataFieldConstants.Composer, "Composer"),
        new(BridgeIdKeys.AppleMusicId, "Apple Music ID"),
    ];

    private static readonly SearchableField[] MovieSearchableFields =
    [
        new(MetadataFieldConstants.Title, "Title", IsDefault: true),
        new(MetadataFieldConstants.Director, "Director"),
        new(MetadataFieldConstants.Year, "Year"),
        new(BridgeIdKeys.TmdbId, "TMDB ID"),
        new(BridgeIdKeys.ImdbId, "IMDb ID"),
    ];

    private static readonly SearchableField[] TvSearchableFields =
    [
        new(MetadataFieldConstants.ShowName, "Show Name", IsDefault: true),
        new(MetadataFieldConstants.SeasonNumber, "Season"),
        new(MetadataFieldConstants.EpisodeNumber, "Episode"),
        new(MetadataFieldConstants.EpisodeTitle, "Episode Title"),
        new(MetadataFieldConstants.Year, "Year"),
        new(BridgeIdKeys.TmdbId, "TMDB ID"),
        new(BridgeIdKeys.ImdbId, "IMDb ID"),
    ];

    private static readonly SearchableField[] ComicSearchableFields =
    [
        new(MetadataFieldConstants.Series, "Series", IsDefault: true),
        new(MetadataFieldConstants.Title, "Title"),
        new(MetadataFieldConstants.SeriesPosition, "Issue #"),
        new(MetadataFieldConstants.Author, "Writer"),
        new(BridgeIdKeys.Isbn, "ISBN"),
        new(BridgeIdKeys.ComicVineId, "Comic Vine ID"),
    ];

    private static readonly SearchableField[] DefaultSearchableFields =
    [
        new(MetadataFieldConstants.Title, "Title", IsDefault: true),
        new(MetadataFieldConstants.Author, "Author"),
    ];
}

/// <summary>
/// Defines how search results are rendered in metadata resolution overlays.
/// One component reads this config and renders the appropriate layout for
/// both retail provider results and Wikidata results.
/// </summary>
/// <param name="Headline">Primary field — displayed as the main title (e.g. title, show_name).</param>
/// <param name="Subline">Secondary field — displayed below headline (e.g. author, artist, director).</param>
/// <param name="Tertiary">Third field — displayed inline after subline (e.g. year, narrator, album).</param>
/// <param name="DetailFields">Additional fields shown as a detail line (e.g. publisher, runtime, duration).</param>
/// <param name="BridgeLabels">Bridge ID keys displayed as chips showing which external IDs are present.</param>
public sealed record SearchDisplayConfig(
    string Headline,
    string Subline,
    string Tertiary,
    IReadOnlyList<string> DetailFields,
    IReadOnlyList<string> BridgeLabels
);

/// <summary>
/// Defines a field available in the search field picker for manual retail matching.
/// </summary>
/// <param name="FieldKey">The metadata claim key (e.g. "title", "author", "isbn").</param>
/// <param name="DisplayLabel">Human-readable label shown in the field picker dropdown.</param>
/// <param name="IsDefault">When true, this field is shown by default in the search form.</param>
public sealed record SearchableField(
    string FieldKey,
    string DisplayLabel,
    bool IsDefault = false
);
