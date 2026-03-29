using MediaEngine.Web.Components.Registry;
using MediaEngine.Web.Models.ViewDTOs;
using MudBlazor;

namespace MediaEngine.Web.Components.Vault;

/// <summary>Static display helpers for Vault components.</summary>
public static class VaultHelpers
{
    /// <summary>Returns the hex color for a VaultStatus.</summary>
    public static string GetVaultStatusColor(VaultStatus status) => status switch
    {
        VaultStatus.Verified => "#5DCAA5",
        VaultStatus.Provisional => "#3B82F6",
        VaultStatus.NeedsReview => "#EF9F27",
        VaultStatus.Failed => "#A05050",
        VaultStatus.Quarantined => "#E24B4A",
        _ => "rgba(255,255,255,0.3)",
    };

    /// <summary>Returns the display label for a VaultStatus.</summary>
    public static string GetVaultStatusLabel(VaultStatus status) => status switch
    {
        VaultStatus.Verified => "Verified",
        VaultStatus.Provisional => "Provisional",
        VaultStatus.NeedsReview => "Needs Review",
        VaultStatus.Failed => "Failed",
        VaultStatus.Quarantined => "Quarantined",
        _ => "Unknown",
    };

    /// <summary>Returns the icon for a VaultStatus.</summary>
    public static string GetVaultStatusIcon(VaultStatus status) => status switch
    {
        VaultStatus.Verified => Icons.Material.Filled.CheckCircle,
        VaultStatus.Provisional => Icons.Material.Filled.Info,
        VaultStatus.NeedsReview => Icons.Material.Filled.Warning,
        VaultStatus.Failed => Icons.Material.Filled.Cancel,
        VaultStatus.Quarantined => Icons.Material.Filled.Block,
        _ => Icons.Material.Filled.HelpOutline,
    };

    /// <summary>Returns the hex color for a pipeline stage state.</summary>
    public static string GetStageColor(VaultStageState state) => state switch
    {
        VaultStageState.Completed => "#5DCAA5",
        VaultStageState.Warning => "#EF9F27",
        VaultStageState.Failed => "#A05050",
        VaultStageState.Pending => "#3B3B3B",
        _ => "#3B3B3B",
    };

    /// <summary>Returns the CSS shadow glow for a pipeline stage state.</summary>
    public static string GetStageShadow(VaultStageState state) => state switch
    {
        VaultStageState.Completed => "0 0 8px rgba(93,202,165,0.3)",
        VaultStageState.Warning => "0 0 8px rgba(239,159,39,0.3)",
        VaultStageState.Failed => "0 0 8px rgba(160,80,80,0.3)",
        _ => "none",
    };

    /// <summary>Returns the confidence bar fill color based on score.</summary>
    public static string GetConfidenceColor(double confidence) => confidence switch
    {
        >= 0.80 => "#5DCAA5",
        >= 0.60 => "#EF9F27",
        _ => "#A05050",
    };

    /// <summary>Delegates to RegistryHelpers for media type icon.</summary>
    public static string GetMediaTypeIcon(string? mediaType) =>
        RegistryHelpers.GetMediaTypeIcon(mediaType);

    /// <summary>Delegates to RegistryHelpers for media type label.</summary>
    public static string FormatMediaType(string? mediaType) =>
        RegistryHelpers.FormatMediaType(mediaType);

    /// <summary>Converts hex color to rgba string. Returns fallback for non-hex input.</summary>
    public static string HexToRgba(string hex, double alpha)
    {
        if (string.IsNullOrEmpty(hex)) return $"rgba(255,255,255,{alpha})";
        // Already an rgba value — just return it
        if (hex.StartsWith("rgba", StringComparison.OrdinalIgnoreCase)) return hex;
        hex = hex.TrimStart('#');
        if (hex.Length < 6) return $"rgba(255,255,255,{alpha})";
        try
        {
            var r = Convert.ToInt32(hex[..2], 16);
            var g = Convert.ToInt32(hex[2..4], 16);
            var b = Convert.ToInt32(hex[4..6], 16);
            return $"rgba({r},{g},{b},{alpha})";
        }
        catch
        {
            return $"rgba(255,255,255,{alpha})";
        }
    }

    /// <summary>Formats file size in human-readable form.</summary>
    public static string FormatFileSize(long? bytes)
    {
        if (bytes is null or 0) return "—";
        return bytes.Value switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes.Value / 1024.0:F1} KB",
            < 1024 * 1024 * 1024 => $"{bytes.Value / (1024.0 * 1024.0):F1} MB",
            _ => $"{bytes.Value / (1024.0 * 1024.0 * 1024.0):F2} GB",
        };
    }

    /// <summary>Returns provider lookup buttons based on media type, matching configured slot providers.</summary>
    public static string[] GetProviderButtons(string? mediaType) => mediaType?.ToUpperInvariant() switch
    {
        "MOVIE" or "MOVIES" => ["TMDB"],
        "TV" => ["TMDB"],
        "BOOK" or "BOOKS" or "EPUB" => ["Open Library", "Google Books"],
        "AUDIOBOOK" or "AUDIOBOOKS" => ["Apple API", "Google Books"],
        "MUSIC" => ["MusicBrainz"],
        "COMICS" or "COMIC" => [],
        "PODCASTS" or "PODCAST" => ["Apple Podcasts", "Podcast Index"],
        _ => [],
    };

    /// <summary>Returns the sort parameter string for the API.</summary>
    public static string GetSortParam(string sortBy) => sortBy switch
    {
        "oldest" => "oldest",
        "title" => "title",
        "title_desc" => "-title",
        "confidence" => "-confidence",
        "confidence_asc" => "confidence",
        "presence" => "-presence",
        "presence_asc" => "presence",
        "name" => "name",
        "name_desc" => "-name",
        _ => "newest",
    };

    /// <summary>Returns a human-readable label for a review trigger code.</summary>
    public static string GetReviewTriggerLabel(string? trigger) => trigger switch
    {
        "AuthorityMatchFailed" => "No provider could identify this item",
        "ContentMatchFailed" => "No matching content found in any provider",
        "StagedUnidentifiable" => "This file could not be identified automatically",
        "PlaceholderTitle" => "The title looks like a placeholder or temporary name",
        "WikidataBridgeFailed" => "Wikidata lookup failed after retail match",
        "RetailMatchFailed" => "No retail provider could find a match",
        "MetadataConflict" => "Multiple sources disagree on this item's metadata",
        "LowConfidence" => "The best match has low confidence",
        "LanguageMismatch" => "File language differs from your library language",
        "DuplicateDetected" => "This may be a duplicate of another item",
        "MediaTypeAmbiguous" => "Could not determine the media type",
        "MissingQid" => "No Wikidata identity found for this item",
        "MultipleQidMatches" => "Multiple possible Wikidata matches found",
        "RootWatchFolder" => "Dropped into root watch folder — please confirm the media type",
        _ => trigger ?? "This item needs review",
    };

    // ── Well-known provider GUIDs → display names ─────────────────────────────

    private static readonly Dictionary<Guid, string> ProviderDisplayNames = new()
    {
        [new("a1b2c3d4-e5f6-4700-8900-0a1b2c3d4e5f")] = "File Scan",
        [new("c9d8e7f6-a5b4-4321-fedc-0102030405c9")] = "Library Scanner",
        [new("b1000001-e000-4000-8000-000000000001")] = "Apple API",
        [new("b2000002-a000-4000-8000-000000000003")] = "Audnexus",
        [new("b3000003-d000-4000-8000-000000000004")] = "Wikidata",
        [new("b4000004-d000-4000-8000-000000000005")] = "Wikipedia",
        [new("b4000004-0000-4000-8000-000000000005")] = "Open Library",
        [new("b5000005-0000-4000-8000-000000000006")] = "Google Books",
        [new("b6000006-0000-4000-8000-000000000007")] = "MusicBrainz",
        [new("b7000007-0000-4000-8000-000000000008")] = "TMDB",
        [new("b8000008-0000-4000-8000-000000000009")] = "Comic Vine",
        [new("b9000009-0000-4000-8000-000000000010")] = "Apple Podcasts",
        [new("ba00000a-0000-4000-8000-000000000011")] = "Podcast Index",
        [new("d0000000-0000-4000-8000-000000000001")] = "Manual Match",
        [new("bb00000b-0000-4000-8000-000000000012")] = "Fanart.tv",
    };

    /// <summary>
    /// Converts a technical source/provider name or GUID to a human-readable display name.
    /// </summary>
    public static string FormatSourceName(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return "Unknown";

        // Try parsing as GUID first (claims use provider_id)
        if (Guid.TryParse(source, out var guid) && ProviderDisplayNames.TryGetValue(guid, out var guidName))
            return guidName;

        return source.ToLowerInvariant() switch
        {
            "user_manual"              => "Manual Match",
            "local_processor"          => "File Scan",
            "file_metadata"            => "File Metadata",
            "wikidata_reconciliation"  => "Wikidata",
            "wikidata"                 => "Wikidata",
            "wikipedia"                => "Wikipedia",
            "retail_provider"          => "Retail Provider",
            "apple_api"                => "Apple API",
            "apple_books"              => "Apple API",
            "open_library"             => "Open Library",
            "google_books"             => "Google Books",
            "musicbrainz"              => "MusicBrainz",
            "tmdb"                     => "TMDB",
            "comic_vine"               => "Comic Vine",
            "apple_podcasts"           => "Apple Podcasts",
            "podcast_index"            => "Podcast Index",
            "fanart_tv"                => "Fanart.tv",
            "audnexus"                 => "Audnexus",
            "library_scanner"          => "Library Scanner",
            _                          => source,
        };
    }

    /// <summary>
    /// Converts a provider GUID to a human-readable display name.
    /// </summary>
    public static string FormatProviderName(Guid providerId)
    {
        return ProviderDisplayNames.TryGetValue(providerId, out var name) ? name : providerId.ToString();
    }

    /// <summary>
    /// Returns true if the given provider GUID represents a file/local source
    /// (local_processor or library_scanner).
    /// </summary>
    public static bool IsFileSource(Guid providerId)
    {
        return providerId == new Guid("a1b2c3d4-e5f6-4700-8900-0a1b2c3d4e5f")
            || providerId == new Guid("c9d8e7f6-a5b4-4321-fedc-0102030405c9");
    }

    /// <summary>
    /// Returns true if the given provider GUID represents a user manual source.
    /// </summary>
    public static bool IsUserSource(Guid providerId)
    {
        return providerId == new Guid("d0000000-0000-4000-8000-000000000001");
    }

    /// <summary>
    /// Builds a clickable external URL for a given bridge ID key and value.
    /// Returns null if no URL template is known for the key.
    /// </summary>
    public static (string Label, string Url)? BuildProviderUrl(string key, string value, string? mediaType = null)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        return key.ToLowerInvariant() switch
        {
            "tmdb_id" when (mediaType ?? "").Contains("TV", StringComparison.OrdinalIgnoreCase)
                => ("View on TMDB", $"https://www.themoviedb.org/tv/{value}"),
            "tmdb_id"
                => ("View on TMDB", $"https://www.themoviedb.org/movie/{value}"),
            "open_library_id" or "olid"
                => ("View on Open Library", $"https://openlibrary.org/works/{value}"),
            "google_books_id"
                => ("View on Google Books", $"https://books.google.com/books?id={value}"),
            "musicbrainz_id"
                => ("View on MusicBrainz", $"https://musicbrainz.org/release/{value}"),
            "wikidata_qid" when value.StartsWith("Q", StringComparison.OrdinalIgnoreCase)
                => ("View on Wikidata", $"https://www.wikidata.org/wiki/{value}"),
            "imdb_id" when value.StartsWith("tt", StringComparison.OrdinalIgnoreCase)
                => ("View on IMDb", $"https://www.imdb.com/title/{value}"),
            "apple_books_id"
                => ("View on Apple Books", $"https://books.apple.com/book/id{value}"),
            _ => null,
        };
    }

    /// <summary>Returns the brand colour for a media type string, matching stats bar colours.</summary>
    public static string GetMediaTypeColor(string? mediaType)
    {
        var t = (mediaType ?? "").ToLowerInvariant();
        if (t.Contains("movie") || t.Contains("video")) return "#60A5FA";
        if (t.Contains("book") && !t.Contains("audio")) return "#5DCAA5";
        if (t.Contains("audiobook")) return "#A78BFA";
        if (t == "tv") return "#FBBF24";
        if (t.Contains("music")) return "#22D3EE";
        if (t.Contains("podcast")) return "#FB923C";
        if (t.Contains("comic")) return "#7C4DFF";
        return "rgba(255,255,255,0.4)";
    }
}
