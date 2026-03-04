using MudBlazor;

namespace Tanaste.Web.Models;

/// <summary>
/// Maps provider config names to unique accent colours and Material icons.
/// Used by the Metadata Prioritization Matrix for visual provider identification.
/// </summary>
public static class ProviderAccentMap
{
    /// <summary>Returns (HexColour, MaterialIcon) for a provider config name.</summary>
    public static (string Color, string Icon) GetAccent(string providerKey) => providerKey switch
    {
        "apple_books_ebook"     => ("#FF2D55", Icons.Material.Filled.MenuBook),
        "apple_books_audiobook" => ("#FF2D55", Icons.Material.Filled.Headphones),
        "audnexus"              => ("#FF9500", Icons.Material.Filled.Hearing),
        "open_library"          => ("#4CAF50", Icons.Material.Filled.LocalLibrary),
        "google_books"          => ("#4285F4", Icons.Material.Filled.Book),
        "wikidata"              => ("#339966", Icons.Material.Filled.Hub),
        "local_filesystem"      => ("#78909C", Icons.Material.Filled.Folder),
        _                       => ("#90A4AE", Icons.Material.Filled.Cloud),
    };

    /// <summary>Returns a deduplicated display name for the UI (e.g. "Apple Books" instead of "apple_books_ebook").</summary>
    public static string GetDisplayName(string providerKey) => providerKey switch
    {
        "apple_books_ebook"     => "Apple Books",
        "apple_books_audiobook" => "Apple Books",
        "audnexus"              => "Audnexus",
        "open_library"          => "Open Library",
        "google_books"          => "Google Books",
        "wikidata"              => "Wikidata",
        "local_filesystem"      => "Local File",
        _                       => FormatProviderName(providerKey),
    };

    private static string FormatProviderName(string key) =>
        string.Join(' ', key.Split('_')
            .Select(w => w.Length > 0 ? char.ToUpperInvariant(w[0]) + w[1..] : w));
}
