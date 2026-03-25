using MudBlazor;

namespace MediaEngine.Web.Models;

/// <summary>
/// Maps provider config names to unique accent colours and Material icons.
/// Used by the Metadata Prioritization Matrix for visual provider identification.
/// </summary>
public static class ProviderAccentMap
{
    /// <summary>Returns whether this provider should be visible in the UI.
    /// Local filesystem is the internal embedded-metadata extractor, not a user-facing provider.</summary>
    public static bool IsVisibleProvider(string providerKey) =>
        !string.Equals(providerKey, "local_filesystem", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns (HexColour, MaterialIcon) for a provider config name.
    /// If <paramref name="customIconName"/> is provided, resolves it from the icon lookup
    /// before falling back to the hardcoded switch.</summary>
    public static (string Color, string Icon) GetAccent(string providerKey, string? customIconName = null)
    {
        var (color, defaultIcon) = providerKey switch
        {
            "apple_api"             => ("#FF2D55", Icons.Material.Filled.MenuBook),
            // audnexus removed - config file deleted as part of SPARQL cleanup
            "open_library"          => ("#4CAF50", Icons.Material.Filled.LocalLibrary),
            "google_books"          => ("#4285F4", Icons.Material.Filled.Book),
            "wikidata"              => ("#339966", Icons.Material.Filled.Hub),
            "tmdb"                  => ("#01B4E4", Icons.Material.Filled.Movie),
            "comic_vine"            => ("#E91E63", Icons.Material.Filled.AutoStories),
            "musicbrainz"           => ("#BA478F", Icons.Material.Filled.MusicNote),
            _                       => ("#90A4AE", Icons.Material.Filled.Cloud),
        };

        if (!string.IsNullOrWhiteSpace(customIconName)
            && MaterialIconLookup.TryGetValue(customIconName, out var resolved))
        {
            return (color, resolved);
        }

        return (color, defaultIcon);
    }

    /// <summary>Ordered list of (Name, Icon) pairs for use in icon picker dropdowns throughout the UI.</summary>
    public static IReadOnlyList<(string Name, string Icon)> IconOptions { get; } =
        new List<(string, string)>
        {
            ("MenuBook",      Icons.Material.Filled.MenuBook),
            ("Headphones",    Icons.Material.Filled.Headphones),
            ("AutoStories",   Icons.Material.Filled.AutoStories),
            ("Movie",         Icons.Material.Filled.Movie),
            ("Tv",            Icons.Material.Filled.Tv),
            ("MusicNote",     Icons.Material.Filled.MusicNote),
            ("Podcasts",      Icons.Material.Filled.Podcasts),
            ("Description",   Icons.Material.Filled.Description),
            ("Folder",        Icons.Material.Filled.Folder),
            ("Photo",         Icons.Material.Filled.Photo),
            ("VideoLibrary",  Icons.Material.Filled.VideoLibrary),
            ("AudioFile",     Icons.Material.Filled.AudioFile),
            ("Article",       Icons.Material.Filled.Article),
            ("Book",          Icons.Material.Filled.Book),
            ("LibraryBooks",  Icons.Material.Filled.LibraryBooks),
            ("LocalLibrary",  Icons.Material.Filled.LocalLibrary),
            ("Hearing",       Icons.Material.Filled.Hearing),
            ("Mic",           Icons.Material.Filled.Mic),
            ("Album",         Icons.Material.Filled.Album),
            ("Camera",        Icons.Material.Filled.Camera),
            ("Image",         Icons.Material.Filled.Image),
            ("PictureAsPdf",  Icons.Material.Filled.PictureAsPdf),
            ("Code",          Icons.Material.Filled.Code),
            ("Science",       Icons.Material.Filled.Science),
            ("School",        Icons.Material.Filled.School),
            ("SportsEsports", Icons.Material.Filled.SportsEsports),
            ("Newspaper",     Icons.Material.Filled.Newspaper),
            ("Dashboard",     Icons.Material.Filled.Dashboard),
            ("Star",          Icons.Material.Filled.Star),
            ("Cloud",         Icons.Material.Filled.Cloud),
            ("Hub",           Icons.Material.Filled.Hub),
        }.AsReadOnly();

    /// <summary>Lookup dictionary mapping common Material icon names to their string constants.</summary>
    private static readonly Dictionary<string, string> MaterialIconLookup = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MenuBook"]      = Icons.Material.Filled.MenuBook,
        ["Headphones"]    = Icons.Material.Filled.Headphones,
        ["AutoStories"]   = Icons.Material.Filled.AutoStories,
        ["Movie"]         = Icons.Material.Filled.Movie,
        ["Tv"]            = Icons.Material.Filled.Tv,
        ["MusicNote"]     = Icons.Material.Filled.MusicNote,
        ["Podcasts"]      = Icons.Material.Filled.Podcasts,
        ["Description"]   = Icons.Material.Filled.Description,
        ["Folder"]        = Icons.Material.Filled.Folder,
        ["Photo"]         = Icons.Material.Filled.Photo,
        ["VideoLibrary"]  = Icons.Material.Filled.VideoLibrary,
        ["AudioFile"]     = Icons.Material.Filled.AudioFile,
        ["Article"]       = Icons.Material.Filled.Article,
        ["Book"]          = Icons.Material.Filled.Book,
        ["LibraryBooks"]  = Icons.Material.Filled.LibraryBooks,
        ["LocalLibrary"]  = Icons.Material.Filled.LocalLibrary,
        ["Hearing"]       = Icons.Material.Filled.Hearing,
        ["Mic"]           = Icons.Material.Filled.Mic,
        ["Album"]         = Icons.Material.Filled.Album,
        ["Camera"]        = Icons.Material.Filled.Camera,
        ["Image"]         = Icons.Material.Filled.Image,
        ["PictureAsPdf"]  = Icons.Material.Filled.PictureAsPdf,
        ["Code"]          = Icons.Material.Filled.Code,
        ["Science"]       = Icons.Material.Filled.Science,
        ["School"]        = Icons.Material.Filled.School,
        ["SportsEsports"] = Icons.Material.Filled.SportsEsports,
        ["Newspaper"]     = Icons.Material.Filled.Newspaper,
        ["Dashboard"]     = Icons.Material.Filled.Dashboard,
        ["Star"]          = Icons.Material.Filled.Star,
        ["Cloud"]         = Icons.Material.Filled.Cloud,
        ["Hub"]           = Icons.Material.Filled.Hub,
    };

    /// <summary>Returns a deduplicated display name for the UI (e.g. "Apple Books" instead of "apple_books").</summary>
    public static string GetDisplayName(string providerKey) => providerKey switch
    {
        "apple_api"             => "Apple API",
        // audnexus removed - config file deleted as part of SPARQL cleanup
        "open_library"          => "Open Library",
        "google_books"          => "Google Books",
        "wikidata"              => "Wikidata",
        "tmdb"                  => "TMDB",
        "comic_vine"            => "Comic Vine",
        "musicbrainz"           => "MusicBrainz",
        _                       => FormatProviderName(providerKey),
    };

    /// <summary>Returns the SVG icon path for a provider (user-provided artwork).</summary>
    public static string GetIconPath(string providerKey) =>
        $"images/providers/{providerKey}.svg";

    /// <summary>Returns the PNG icon path for a provider (user-supplied branding).
    /// Falls back to the Material icon via <see cref="GetAccent"/> when the PNG does not exist.</summary>
    public static string GetIconImagePath(string providerKey) =>
        $"images/providers/{providerKey}.png";

    private static string FormatProviderName(string key) =>
        string.Join(' ', key.Split('_')
            .Select(w => w.Length > 0 ? char.ToUpperInvariant(w[0]) + w[1..] : w));
}
