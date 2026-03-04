using MudBlazor;

namespace Tanaste.Web.Models;

/// <summary>
/// Static reference dictionary mapping canonical claim keys to human-readable
/// display names and categories. Mirrors <c>config/field_normalization.json</c>.
/// </summary>
public static class FieldDictionary
{
    /// <summary>Display metadata for a single field.</summary>
    public sealed record FieldInfo(string DisplayName, string Category, string Icon);

    /// <summary>
    /// Master field map keyed by canonical claim key.
    /// Categories: Core, People, Lore, Bridge.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, FieldInfo> Fields =
        new Dictionary<string, FieldInfo>(StringComparer.OrdinalIgnoreCase)
        {
            // ── Core ─────────────────────────────────────────────────────────
            ["title"]           = new("Title",           "Core",   Icons.Material.Filled.Title),
            ["author"]          = new("Author",          "Core",   Icons.Material.Filled.Person),
            ["year"]            = new("Year",            "Core",   Icons.Material.Filled.CalendarMonth),
            ["description"]     = new("Description",     "Core",   Icons.Material.Filled.Description),
            ["cover"]           = new("Cover Art",       "Core",   Icons.Material.Filled.Image),
            ["rating"]          = new("Rating",          "Core",   Icons.Material.Filled.Star),
            ["isbn"]            = new("ISBN",            "Core",   Icons.Material.Filled.QrCode),
            ["asin"]            = new("ASIN",            "Core",   Icons.Material.Filled.QrCode2),
            ["series"]          = new("Series",          "Core",   Icons.Material.Filled.LibraryBooks),
            ["series_position"] = new("Series Position", "Core",   Icons.Material.Filled.FormatListNumbered),
            ["publisher"]       = new("Publisher",       "Core",   Icons.Material.Filled.Business),
            ["page_count"]      = new("Page Count",      "Core",   Icons.Material.Filled.MenuBook),
            ["genre"]           = new("Genre",           "Core",   Icons.Material.Filled.Category),
            ["language"]        = new("Language",        "Core",   Icons.Material.Filled.Translate),

            // ── People ───────────────────────────────────────────────────────
            ["narrator"]        = new("Narrator",        "People", Icons.Material.Filled.RecordVoiceOver),
            ["director"]        = new("Director",        "People", Icons.Material.Filled.Movie),
            ["cast_member"]     = new("Cast",            "People", Icons.Material.Filled.Groups),
            ["illustrator"]     = new("Illustrator",     "People", Icons.Material.Filled.Brush),
            ["voice_actor"]     = new("Voice Actor",     "People", Icons.Material.Filled.Mic),
            ["screenwriter"]    = new("Screenwriter",    "People", Icons.Material.Filled.EditNote),
            ["composer"]        = new("Composer",        "People", Icons.Material.Filled.MusicNote),

            // ── Lore ─────────────────────────────────────────────────────────
            ["franchise"]          = new("Franchise",    "Lore",   Icons.Material.Filled.AccountTree),
            ["characters"]         = new("Characters",   "Lore",   Icons.Material.Filled.Face),
            ["narrative_location"] = new("Setting",      "Lore",   Icons.Material.Filled.Place),
            ["main_subject"]       = new("Subject",      "Lore",   Icons.Material.Filled.Topic),
            ["fictional_universe"] = new("Universe",     "Lore",   Icons.Material.Filled.Public),

            // ── Bridge ───────────────────────────────────────────────────────
            ["wikidata_qid"]    = new("Wikidata QID",    "Bridge", Icons.Material.Filled.Hub),
            ["tmdb_id"]         = new("TMDB ID",         "Bridge", Icons.Material.Filled.Theaters),
            ["imdb_id"]         = new("IMDb ID",         "Bridge", Icons.Material.Filled.LocalMovies),
            ["goodreads_id"]    = new("Goodreads ID",    "Bridge", Icons.Material.Filled.AutoStories),
            ["apple_books_id"]  = new("Apple Books ID",  "Bridge", Icons.Material.Filled.MenuBook),
            ["audible_id"]      = new("Audible ID",      "Bridge", Icons.Material.Filled.Headphones),
            ["musicbrainz_id"]  = new("MusicBrainz ID",  "Bridge", Icons.Material.Filled.Album),
            ["spotify_id"]      = new("Spotify ID",      "Bridge", Icons.Material.Filled.GraphicEq),
            ["comicvine_id"]    = new("ComicVine ID",    "Bridge", Icons.Material.Filled.MenuBook),
        };

    /// <summary>Claim keys that form the "Universe Information" group in the Prioritization Matrix.</summary>
    public static readonly HashSet<string> UniverseGroupFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "series", "series_position", "franchise", "wikidata_qid",
        "characters", "narrative_location", "main_subject", "fictional_universe",
        "tmdb_id", "imdb_id", "goodreads_id", "apple_books_id", "audible_id",
        "isbn", "asin", "comicvine_id", "musicbrainz_id", "spotify_id",
    };

    /// <summary>Returns the default columns for the Metadata Prioritization Matrix.</summary>
    public static List<MatrixColumn> GetDefaultColumns() =>
    [
        new() { ZoneId = "cover",         DisplayName = "Cover Art",            Icon = Icons.Material.Filled.Image,         ClaimKeys = ["cover"],               IsGroup = false, SortOrder = 0 },
        new() { ZoneId = "description",   DisplayName = "Description",          Icon = Icons.Material.Filled.Description,   ClaimKeys = ["description"],         IsGroup = false, SortOrder = 1 },
        new() { ZoneId = "title",         DisplayName = "Title",                Icon = Icons.Material.Filled.Title,         ClaimKeys = ["title"],               IsGroup = false, SortOrder = 2 },
        new() { ZoneId = "year",          DisplayName = "Year",                 Icon = Icons.Material.Filled.CalendarMonth, ClaimKeys = ["year"],                IsGroup = false, SortOrder = 3 },
        new() { ZoneId = "rating",        DisplayName = "Rating",               Icon = Icons.Material.Filled.Star,          ClaimKeys = ["rating"],              IsGroup = false, SortOrder = 4 },
        new() { ZoneId = "universe_info", DisplayName = "Universe Information", Icon = Icons.Material.Filled.Public,        ClaimKeys = [.. UniverseGroupFields], IsGroup = true, SortOrder = int.MaxValue, IsPinned = true },
    ];

    /// <summary>Gets the display name for a claim key, or formats it as title case if unknown.</summary>
    public static string GetDisplayName(string claimKey)
    {
        if (Fields.TryGetValue(claimKey, out var info))
            return info.DisplayName;

        // Fallback: convert snake_case to Title Case.
        return string.Join(' ', claimKey.Split('_')
            .Select(w => w.Length > 0
                ? char.ToUpperInvariant(w[0]) + w[1..]
                : w));
    }

    /// <summary>Gets the icon for a claim key, or a default icon if unknown.</summary>
    public static string GetIcon(string claimKey)
    {
        return Fields.TryGetValue(claimKey, out var info)
            ? info.Icon
            : Icons.Material.Filled.DataObject;
    }

    /// <summary>Gets the category for a claim key, or "Other" if unknown.</summary>
    public static string GetCategory(string claimKey)
    {
        return Fields.TryGetValue(claimKey, out var info)
            ? info.Category
            : "Other";
    }
}
