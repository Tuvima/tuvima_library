using MediaEngine.Web.Models.ViewDTOs;
using MudBlazor;

namespace MediaEngine.Web.Components.Registry;

/// <summary>Static display helpers shared across Registry components.</summary>
public static class RegistryHelpers
{
    public static string GetMediaTypeIcon(string? mediaType) => mediaType?.ToUpperInvariant() switch
    {
        "EPUB" or "BOOK" or "BOOKS" => Icons.Material.Outlined.MenuBook,
        "AUDIOBOOK" or "AUDIOBOOKS" => Icons.Material.Outlined.Headphones,
        "MOVIES" or "MOVIE" => Icons.Material.Outlined.Movie,
        "TV" => Icons.Material.Outlined.Tv,
        "MUSIC" => Icons.Material.Outlined.MusicNote,
        "COMICS" or "COMIC" => Icons.Material.Outlined.AutoStories,
        "PODCASTS" or "PODCAST" => Icons.Material.Outlined.Podcasts,
        "UNIVERSE" => Icons.Material.Outlined.Hub,
        _ => Icons.Material.Outlined.InsertDriveFile,
    };

    public static string FormatMediaType(string? mediaType) => mediaType?.ToUpperInvariant() switch
    {
        "EPUB" or "BOOKS" or "BOOK"      => "Book",
        "AUDIOBOOK" or "AUDIOBOOKS"      => "Audiobook",
        "MOVIES" or "MOVIE"              => "Movie",
        "TV"                             => "TV",
        "MUSIC"                          => "Music",
        "COMICS" or "COMIC"              => "Comic",
        "PODCASTS" or "PODCAST"          => "Podcast",
        _ => mediaType ?? "—",
    };

    public static Color GetConfidenceColor(double conf) => conf switch
    {
        >= 0.85 => Color.Success,
        >= 0.60 => Color.Warning,
        _ => Color.Error,
    };

    public static string GetConfidenceHexColor(double conf) => conf switch
    {
        >= 0.85 => "#4CAF50",
        >= 0.60 => "#EAB308",
        _ => "#EF5350",
    };

    public static string GetConfidenceLabel(double conf) => conf switch
    {
        >= 0.85 => "High",
        >= 0.60 => "Medium",
        _ => "Low",
    };

    public static string GetStatusChipStyle(string status) => status switch
    {
        "Auto" => "background: rgba(76,175,80,0.15); color: #4CAF50; font-size: 0.7rem; height: 22px;",
        "Review" => "background: rgba(234,179,8,0.15); color: #EAB308; font-size: 0.7rem; height: 22px;",
        "Edited" => "background: rgba(66,165,245,0.15); color: #42A5F5; font-size: 0.7rem; height: 22px;",
        "Duplicate" => "background: rgba(239,83,80,0.15); color: #EF5350; font-size: 0.7rem; height: 22px;",
        _ => "background: rgba(255,255,255,0.05); color: #888; font-size: 0.7rem; height: 22px;",
    };

    public static (string Label, string Color, string Icon) FormatReviewTrigger(string? trigger) => trigger switch
    {
        "LowConfidence"         => ("Needs Verification",     "#FF9800", Icons.Material.Outlined.TrendingDown),
        "MultipleQidMatches"    => ("Multiple Matches Found", "#2196F3", Icons.Material.Outlined.CallSplit),
        "AuthorityMatchFailed"  => ("No Match Found",         "#F44336", Icons.Material.Outlined.LinkOff),
        "ContentMatchFailed"    => ("No Cover Art Found",     "#F44336", Icons.Material.Outlined.SearchOff),
        "AmbiguousMediaType"    => ("Unknown Format",         "#FF9800", Icons.Material.Outlined.HelpOutline),
        "MissingQid"            => ("Needs Manual Match",     "#2196F3", Icons.Material.Outlined.QuestionMark),
        "PlaceholderTitle"      => ("Missing Title",          "#F44336", Icons.Material.Outlined.Title),
        "StagedUnidentifiable"  => ("Cannot Identify",        "#F44336", Icons.Material.Outlined.ErrorOutline),
        "ArtworkUnconfirmed"    => ("Verify Cover Art",       "#FF9800", Icons.Material.Outlined.Image),
        "MetadataConflict"      => ("Conflicting Data",       "#FF9800", Icons.Material.Outlined.Compare),
        "UserFixMatch"          => ("User Correction",        "#2196F3", Icons.Material.Outlined.Edit),
        "ArbiterNeedsReview"    => ("Review Grouping",        "#FF9800", Icons.Material.Outlined.Gavel),
        "NonConfiguredLanguage" => ("Foreign Language",       "#FF9800", Icons.Material.Outlined.Translate),
        "LanguageMismatch"      => ("Foreign Language",       "#FF9800", Icons.Material.Filled.Translate),
        _ => ("Needs Review", "#9E9E9E", Icons.Material.Outlined.RateReview),
    };

    public static string FormatBridgeKey(string key) => key switch
    {
        "isbn"           => "ISBN",
        "isbn_13"        => "ISBN-13",
        "isbn_10"        => "ISBN-10",
        "asin"           => "ASIN",
        "tmdb_id"        => "TMDB",
        "imdb_id"        => "IMDb",
        "wikidata_qid"   => "Wikidata",
        "apple_books_id" => "Apple API",
        "audible_id"     => "Audible",
        "goodreads_id"   => "Goodreads",
        "musicbrainz_id" => "MusicBrainz",
        "comic_vine_id"  => "Comic Vine",
        _ => key.Replace("_", " ").ToUpperInvariant(),
    };

    /// <summary>Returns the most important status badge text for the simple list view.</summary>
    public static (string Text, string Style) GetPrimaryStatusBadge(RegistryItemViewModel item)
    {
        // Priority: review trigger > low confidence > missing art > status
        if (!string.IsNullOrEmpty(item.ReviewTrigger))
        {
            var (label, color, _) = FormatReviewTrigger(item.ReviewTrigger);
            return (label, $"background: {HexToRgba(color, 0.15)}; color: {color};");
        }

        if (item.Confidence < 0.85 && item.Confidence > 0)
        {
            var color = GetConfidenceHexColor(item.Confidence);
            return ($"Low Confidence {(int)(item.Confidence * 100)}%", $"background: {HexToRgba(color, 0.15)}; color: {color};");
        }

        if (string.IsNullOrEmpty(item.CoverUrl))
            return ("Missing Art", "background: rgba(239,83,80,0.15); color: #EF5350;");

        if (item.MissingUniverse)
            return ("No Match", "background: rgba(158,158,158,0.15); color: #9E9E9E;");

        return (item.Status, GetStatusChipStyle(item.Status));
    }

    private static string HexToRgba(string hex, double alpha)
    {
        hex = hex.TrimStart('#');
        if (hex.Length < 6) return $"rgba(255,255,255,{alpha})";
        var r = Convert.ToInt32(hex[..2], 16);
        var g = Convert.ToInt32(hex[2..4], 16);
        var b = Convert.ToInt32(hex[4..6], 16);
        return $"rgba({r},{g},{b},{alpha})";
    }
}
