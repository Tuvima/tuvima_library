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

    /// <summary>
    /// The Wikidata Reconciliation provider GUID.
    /// Used to identify Wikidata claims in the priority cascade.
    /// </summary>
    public static readonly Guid WikidataProviderId =
        Guid.Parse("b3000003-d000-4000-8000-000000000004");

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
