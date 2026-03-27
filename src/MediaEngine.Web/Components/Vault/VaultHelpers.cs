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

    /// <summary>Returns provider lookup buttons based on media type.</summary>
    public static string[] GetProviderButtons(string? mediaType) => mediaType?.ToUpperInvariant() switch
    {
        "MOVIE" or "MOVIES" => ["TMDB", "IMDb"],
        "TV" => ["TMDB", "IMDb"],
        "BOOK" or "BOOKS" or "EPUB" => ["Open Library", "Apple Books"],
        "AUDIOBOOK" or "AUDIOBOOKS" => ["Audible", "Apple Books"],
        "MUSIC" => ["MusicBrainz", "Spotify"],
        "COMICS" or "COMIC" => ["Comic Vine"],
        "PODCASTS" or "PODCAST" => ["Apple Podcasts"],
        _ => ["Global Search", "MusicBrainz", "Audible"],
    };

    /// <summary>Returns the sort parameter string for the API.</summary>
    public static string GetSortParam(string sortBy) => sortBy switch
    {
        "oldest" => "oldest",
        "confidence" => "confidence",
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
        _ => trigger ?? "This item needs review",
    };

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
