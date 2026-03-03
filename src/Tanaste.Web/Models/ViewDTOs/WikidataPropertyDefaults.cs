namespace Tanaste.Web.Models.ViewDTOs;

/// <summary>
/// Shared static property data for Wikidata universe properties.
/// Used by both <c>WikidataVaultPanel</c> and <c>UniverseSettingsTab</c>
/// to eliminate duplication of hardcoded property lists.
/// </summary>
public static class WikidataPropertyDefaults
{
    /// <summary>A single Wikidata property used by the Universe knowledge model.</summary>
    public sealed record WikidataPropertyInfo(
        string PCode, string ClaimKey, string Category,
        string EntityScope, double Confidence, bool IsBridge, bool Enabled,
        string StageApplicability = "Media Match");

    /// <summary>A bridge identifier entry for QID cross-referencing.</summary>
    public sealed record BridgeEntry(string PCode, string RequestField);

    /// <summary>Returns all default Wikidata properties across all categories.</summary>
    public static List<WikidataPropertyInfo> GetAllProperties() =>
    [
        // Core Identity
        new("P31",   "instance_of",       "Core Identity", "Work",   0.9,  false, true),
        new("P1476", "title",             "Core Identity", "Work",   0.9,  false, true),
        new("P179",  "series",            "Core Identity", "Work",   0.9,  false, true),
        new("P1545", "series_position",   "Core Identity", "Work",   0.9,  false, true),
        new("P8345", "franchise",         "Core Identity", "Work",   0.9,  false, true),
        new("P577",  "year",              "Core Identity", "Work",   0.9,  false, true),
        new("P136",  "genre",             "Core Identity", "Work",   0.85, false, true),
        new("P7937", "form_of_work",      "Core Identity", "Work",   0.85, false, true),
        new("P364",  "original_language", "Core Identity", "Work",   0.85, false, true),
        new("P495",  "country_of_origin", "Core Identity", "Work",   0.85, false, true),

        // People
        new("P50",   "author",        "People", "Work",   0.9,  false, true),
        new("P110",  "illustrator",   "People", "Work",   0.9,  false, true),
        new("P57",   "director",      "People", "Work",   0.9,  false, true),
        new("P58",   "screenwriter",  "People", "Work",   0.9,  false, true),
        new("P161",  "cast_member",   "People", "Work",   0.9,  false, true),
        new("P86",   "composer",      "People", "Work",   0.9,  false, true),
        new("P18",   "headshot_url",  "People", "Person", 0.9,  false, true, "Human Hub"),
        new("P106",  "occupation",    "People", "Person", 0.85, false, true, "Human Hub"),
        new("P569",  "date_of_birth", "People", "Person", 0.9,  false, true, "Human Hub"),
        new("P570",  "date_of_death", "People", "Person", 0.9,  false, true, "Human Hub"),
        new("P27",   "citizenship",   "People", "Person", 0.85, false, true, "Human Hub"),

        // Lore & Narrative
        new("P674",  "characters",         "Lore & Narrative", "Work", 0.85, false, true),
        new("P840",  "narrative_location",  "Lore & Narrative", "Work", 0.85, false, true),
        new("P1441", "present_in_work",     "Lore & Narrative", "Work", 0.8,  false, true),
        new("P144",  "based_on",            "Lore & Narrative", "Work", 0.85, false, true),
        new("P4969", "derivative_work",     "Lore & Narrative", "Work", 0.8,  false, true),

        // Bridges: Books
        new("P3861", "apple_books_id", "Bridges: Books", "Work", 1.0, true, true),
        new("P212",  "isbn",           "Bridges: Books", "Work", 1.0, true, true),
        new("P2969", "goodreads_id",   "Bridges: Books", "Work", 1.0, true, true),
        new("P648",  "openlibrary_id", "Bridges: Books", "Work", 1.0, true, true),
        new("P8383", "storygraph_id",  "Bridges: Books", "Work", 1.0, true, true),

        // Bridges: Movies/TV
        new("P4947", "tmdb_id",    "Bridges: Movies/TV", "Work", 1.0, true, true),
        new("P345",  "imdb_id",    "Bridges: Movies/TV", "Work", 1.0, true, true),
        new("P1566", "asin",       "Bridges: Movies/TV", "Work", 1.0, true, true),
        new("P3398", "audible_id", "Bridges: Movies/TV", "Work", 1.0, true, true),

        // Social Pivot
        new("P2003", "instagram", "Social Pivot", "Person", 0.9, false, true, "Human Hub"),
        new("P2002", "twitter",   "Social Pivot", "Person", 0.9, false, true, "Human Hub"),
        new("P7085", "tiktok",    "Social Pivot", "Person", 0.9, false, true, "Human Hub"),
        new("P856",  "website",   "Social Pivot", "Person", 0.9, false, true, "Human Hub"),
    ];

    /// <summary>Returns bridge identifier entries used for QID cross-referencing.</summary>
    public static List<BridgeEntry> GetBridgeEntries() =>
    [
        new("P3861", "apple_books_id"),
        new("P3398", "audible_id"),
        new("P4947", "tmdb_id"),
        new("P345",  "imdb_id"),
        new("P1566", "asin"),
        new("P212",  "isbn"),
    ];

    /// <summary>Returns human-readable label for a bridge P-code.</summary>
    public static string GetBridgeLabel(string pCode) => pCode switch
    {
        "P3861" => "Apple Books ID",
        "P3398" => "Audible ID",
        "P4947" => "TMDB Movie ID",
        "P345"  => "IMDb ID",
        "P1566" => "ASIN",
        "P212"  => "ISBN-13",
        _       => pCode,
    };

    /// <summary>Returns the target provider name for a bridge P-code.</summary>
    public static string GetTargetProvider(string pCode) => pCode switch
    {
        "P3861" => "Apple Books",
        "P3398" => "Audnexus",
        "P4947" => "TMDB",
        "P345"  => "IMDb",
        "P1566" => "Amazon",
        "P212"  => "Open Library",
        _       => "Unknown",
    };

    /// <summary>Returns category sort order.</summary>
    public static int CategoryOrder(string category) => category switch
    {
        "Core Identity"          => 0,
        "People"                 => 1,
        "Lore & Narrative"       => 2,
        "Bridges: Books"         => 3,
        "Bridges: Movies/TV"     => 4,
        "Bridges: Comics/Anime"  => 5,
        "Bridges: Music/Audio"   => 6,
        "Social Pivot"           => 7,
        _                        => 99,
    };

    /// <summary>Returns the category icon for display.</summary>
    public static string GetCategoryIcon(string category) => category switch
    {
        "Core Identity"          => "identification",
        "People"                 => "people",
        "Lore & Narrative"       => "auto_stories",
        "Bridges: Books"         => "menu_book",
        "Bridges: Movies/TV"     => "movie",
        "Bridges: Comics/Anime"  => "comic_bubble",  // Not in MudBlazor — fall back
        "Bridges: Music/Audio"   => "music_note",
        "Social Pivot"           => "share",
        _                        => "category",
    };
}
