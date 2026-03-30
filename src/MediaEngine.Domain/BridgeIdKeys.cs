namespace MediaEngine.Domain;

/// <summary>
/// Single source of truth for bridge ID key names — the external identifier
/// types stored in the <c>bridge_ids</c> table and used throughout the
/// hydration pipeline to link media to external databases.
/// </summary>
public static class BridgeIdKeys
{
    public const string Isbn = "isbn";
    public const string Isbn13 = "isbn_13";
    public const string Isbn10 = "isbn_10";
    public const string Asin = "asin";
    public const string TmdbId = "tmdb_id";
    public const string ImdbId = "imdb_id";
    public const string WikidataQid = "wikidata_qid";
    public const string AppleBooksId = "apple_books_id";
    public const string AudibleId = "audible_id";
    public const string GoodreadsId = "goodreads_id";
    public const string MusicBrainzId = "musicbrainz_id";
    public const string ComicVineId = "comic_vine_id";
    public const string SpotifyId = "spotify_id";
    public const string PodcastIndexId = "podcast_index_id";
    public const string OpenLibraryId = "open_library_id";

    /// <summary>All known bridge ID keys, for validation and enumeration.</summary>
    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Isbn, Isbn13, Isbn10, Asin, TmdbId, ImdbId, WikidataQid,
        AppleBooksId, AudibleId, GoodreadsId, MusicBrainzId,
        ComicVineId, SpotifyId, PodcastIndexId, OpenLibraryId
    };
}
