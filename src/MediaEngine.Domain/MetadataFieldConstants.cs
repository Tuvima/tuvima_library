namespace MediaEngine.Domain;

/// <summary>
/// Single source of truth for metadata field key constants used across the Engine.
/// All multi-valued field detection should reference <see cref="MultiValuedKeys"/>
/// instead of maintaining local copies.
/// </summary>
public static class MetadataFieldConstants
{
    /// <summary>
    /// Suffix appended to a multi-valued claim key to form its companion QID key.
    /// Example: "genre" → "genre_qid".
    /// </summary>
    public const string CompanionQidSuffix = "_qid";

    /// <summary>Use <see cref="WellKnownProviders.Wikidata"/> instead.</summary>
    [Obsolete("Use WellKnownProviders.Wikidata instead.")]
    public static readonly Guid WikidataProviderId = WellKnownProviders.Wikidata;

    /// <summary>Use <see cref="WellKnownProviders.AiProvider"/> instead.</summary>
    [Obsolete("Use WellKnownProviders.AiProvider instead.")]
    public static readonly Guid AiProviderId = WellKnownProviders.AiProvider;

    // ── Single-valued claim keys ──────────────────────────────────────────────
    // These are the canonical key names stored in the metadata_claims table.
    // All claim creation and field lookups must use these constants.

    public const string Title           = "title";
    public const string Author          = "author";
    public const string Year            = "year";
    public const string Description     = "description";
    public const string Cover           = "cover";
    public const string Rating          = "rating";
    public const string Series          = "series";
    public const string SeriesPosition  = "series_position";
    public const string Runtime         = "runtime";
    public const string Album           = "album";
    public const string Artist          = "artist";
    public const string OriginalTitle   = "original_title";
    public const string SeasonNumber    = "season_number";
    public const string EpisodeNumber   = "episode_number";
    public const string TrackNumber     = "track_number";
    public const string MediaTypeField  = "media_type";
    public const string PublisherField  = "publisher";
    public const string PageCount       = "page_count";
    public const string Language        = "language";
    public const string DurationField   = "duration";
    public const string Franchise       = "franchise";
    public const string CustomTags      = "custom_tags";
    public const string CoverUrl        = "cover_url";
    public const string ShowName        = "show_name";
    public const string EpisodeTitle    = "episode_title";
    public const string PodcastName     = "podcast_name";

    // ── Multi-valued claim keys also used in claim creation ──────────────────
    public const string Narrator        = "narrator";
    public const string Director        = "director";
    public const string Genre           = "genre";
    public const string Illustrator     = "illustrator";
    public const string CastMember      = "cast_member";
    public const string Composer        = "composer";
    public const string Screenwriter    = "screenwriter";

    /// <summary>
    /// Multi-valued field keys that may contain multiple values from Wikidata
    /// or other providers. These keys are decomposed into individual
    /// <c>CanonicalArrayEntry</c> rows in the array repository.
    /// </summary>
    public static readonly HashSet<string> MultiValuedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        // Core multi-valued fields
        "genre",
        "characters",
        "cast_member",
        "voice_actor",
        "narrative_location",
        "main_subject",
        "composer",
        "screenwriter",
        "author",
        "narrator",
        "director",
        "illustrator",
        "nationality",
        "occupation",
        "pseudonym",
        "attributed_to",
        "notable_work",
        "based_on",
        "fictional_universe",
        "first_appearance",

        // AI-generated vocabulary fields
        "themes",
        "mood",
        "content_warnings",

        // Companion QID keys (paired with above)
        "genre_qid",
        "characters_qid",
        "cast_member_qid",
        "voice_actor_qid",
        "narrative_location_qid",
        "main_subject_qid",
        "composer_qid",
        "screenwriter_qid",
        "author_qid",
        "narrator_qid",
        "director_qid",
        "illustrator_qid",
        "based_on_qid",
        "fictional_universe_qid",
    };

    /// <summary>
    /// Returns <c>true</c> if the given claim key is multi-valued.
    /// </summary>
    public static bool IsMultiValued(string key) =>
        MultiValuedKeys.Contains(key);
}
