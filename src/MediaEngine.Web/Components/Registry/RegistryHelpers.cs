using System.IO;
using MediaEngine.Domain;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Services;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Theming;
using MudBlazor;

namespace MediaEngine.Web.Components.Registry;

/// <summary>Static display helpers shared across Registry components.</summary>
public static class RegistryHelpers
{
    public static string GetMediaTypeIcon(string? mediaType)
    {
        var upper = mediaType?.ToUpperInvariant();
        if (upper is "UNIVERSE") return Icons.Material.Outlined.AutoAwesome;
        if (upper is "PERSON" or "PEOPLE") return Icons.Material.Outlined.Person;

        var type = MediaTypeClassifier.Classify(mediaType);
        return type switch
        {
            MediaType.Books      => Icons.Material.Outlined.MenuBook,
            MediaType.Audiobooks => Icons.Material.Outlined.Headphones,
            MediaType.Movies     => Icons.Material.Outlined.Movie,
            MediaType.TV         => Icons.Material.Outlined.Tv,
            MediaType.Music      => Icons.Material.Outlined.MusicNote,
            MediaType.Comics     => Icons.Material.Outlined.AutoStories,
            MediaType.Podcasts   => Icons.Material.Outlined.Podcasts,
            _                    => Icons.Material.Outlined.InsertDriveFile,
        };
    }

    public static string FormatMediaType(string? mediaType)
    {
        // Handle special UI entity types not in the Domain enum
        var upper = mediaType?.ToUpperInvariant();
        if (upper is "PERSON" or "PEOPLE") return "Person";
        if (upper is "UNIVERSE" or "UNIVERSES") return "Universe";
        return MediaTypeClassifier.GetDisplayLabel(mediaType);
    }

    public static Color GetConfidenceColor(double conf) => conf switch
    {
        >= 0.85 => Color.Success,
        >= 0.60 => Color.Warning,
        _ => Color.Error,
    };

    public static string GetConfidenceHexColor(double conf)
    {
        var p = PaletteProvider.Current.Confidence;
        return conf switch
        {
            >= 0.85 => p.High,
            >= 0.60 => p.Medium,
            _       => p.Low,
        };
    }

    public static string GetConfidenceLabel(double conf) => conf switch
    {
        >= 0.85 => "High",
        >= 0.60 => "Medium",
        _ => "Low",
    };

    public static string GetStatusChipStyle(string status) => status switch
    {
        "Identified" => "background: rgba(76,175,80,0.15); color: #4CAF50; font-size: 0.7rem; height: 22px;",
        "Auto" => "background: rgba(90,138,94,0.15); color: #5A8A5E; font-size: 0.7rem; height: 22px;",
        "Review" => "background: rgba(176,137,64,0.15); color: #B08940; font-size: 0.7rem; height: 22px;",
        "Edited" => "background: rgba(92,122,153,0.15); color: #5C7A99; font-size: 0.7rem; height: 22px;",
        "Duplicate" => "background: rgba(160,80,80,0.15); color: #A05050; font-size: 0.7rem; height: 22px;",
        _ => "background: rgba(255,255,255,0.05); color: #6B6B6B; font-size: 0.7rem; height: 22px;",
    };

    public static (string Label, string Color, string Icon) FormatReviewTrigger(string? trigger)
    {
        var p = PaletteProvider.Current.ReviewTrigger;
        // Muted cinematic palette: warm amber, cool slate, soft rose — never raw Material Design.
        return trigger switch
        {
            "LowConfidence"        => ("Needs Verification",     p.LowConfidence,   Icons.Material.Outlined.TrendingDown),
            "MultipleQidMatches"   => ("Multiple Matches Found", p.MultipleMatches, Icons.Material.Outlined.CallSplit),
            "AuthorityMatchFailed" => ("No Match Found",         p.MatchFailed,     Icons.Material.Outlined.LinkOff),
            "RetailMatchFailed"    => ("No Retail Match",        p.MatchFailed,     Icons.Material.Outlined.LinkOff),
            "ContentMatchFailed"   => ("No Cover Art Found",     p.MatchFailed,     Icons.Material.Outlined.SearchOff),
            "AmbiguousMediaType"   => ("Unknown Format",         p.Ambiguous,       Icons.Material.Outlined.HelpOutline),
            "MissingQid"           => ("Needs Manual Match",     p.MultipleMatches, Icons.Material.Outlined.QuestionMark),
            "PlaceholderTitle"     => ("Missing Title",          p.MatchFailed,     Icons.Material.Outlined.Title),
            "StagedUnidentifiable" => ("Cannot Identify",        p.MatchFailed,     Icons.Material.Outlined.ErrorOutline),
            "ArtworkUnconfirmed"   => ("Verify Cover Art",       p.Ambiguous,       Icons.Material.Outlined.Image),
            "MetadataConflict"     => ("Conflicting Data",       p.Ambiguous,       Icons.Material.Outlined.Compare),
            "UserFixMatch"         => ("User Correction",        p.MultipleMatches, Icons.Material.Outlined.Edit),
            "ArbiterNeedsReview"   => ("Review Grouping",        p.Ambiguous,       Icons.Material.Outlined.Gavel),
            "LanguageMismatch"     => ("Foreign Language",       p.Ambiguous,       Icons.Material.Filled.Translate),
            "UserReport"           => ("User Report",            p.UserReport,      Icons.Material.Filled.Flag),
            "WikidataBridgeFailed" => ("Wikidata Match Failed",  p.MatchFailed,     Icons.Material.Outlined.CloudOff),
            _                      => ("Needs Review",           p.Default,         Icons.Material.Outlined.RateReview),
        };
    }

    public static string FormatBridgeKey(string key) => key switch
    {
        BridgeIdKeys.Isbn        => "ISBN",
        BridgeIdKeys.Isbn13      => "ISBN-13",
        BridgeIdKeys.Isbn10      => "ISBN-10",
        BridgeIdKeys.Asin        => "ASIN",
        BridgeIdKeys.TmdbId      => "TMDB",
        BridgeIdKeys.ImdbId      => "IMDb",
        BridgeIdKeys.WikidataQid => "Wikidata",
        BridgeIdKeys.AppleBooksId => "Apple API",
        BridgeIdKeys.AudibleId   => "Audible",
        BridgeIdKeys.GoodreadsId => "Goodreads",
        BridgeIdKeys.MusicBrainzId => "MusicBrainz",
        BridgeIdKeys.ComicVineId => "Comic Vine",
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

        if (item.Status == "Identified" && !item.HasUserLocks)
            return ("Awaiting Match", "background: rgba(176,137,64,0.15); color: #B08940;");

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

    // ── Status helpers (new 4-state model) ────────────────────────────────

    /// <summary>Returns the hex color for the four-state registry model.</summary>
    public static string GetStatusColor(string status)
    {
        var p = PaletteProvider.Current.Status;
        return status switch
        {
            "Identified"     => p.Identified,
            "Confirmed"      => p.Confirmed,
            "InReview"       => p.InReview,
            "Provisional"    => p.Provisional,
            "Rejected"       => p.Rejected,
            "Known"          => p.Known,
            "AwaitingStage2" => p.AwaitingStage2,
            // Legacy statuses (backward compat during migration)
            "Review"     => p.InReview,
            "Auto"       => p.Identified,
            "Edited"     => p.Identified,
            "Staging"    => p.InReview,
            "Duplicate"  => p.InReview,
            _            => p.Default,
        };
    }

    /// <summary>Returns the icon for a registry status badge.</summary>
    public static string GetStatusIcon(string status) => status switch
    {
        "Identified"     => Icons.Material.Filled.CheckCircle,
        "Confirmed"      => Icons.Material.Filled.VerifiedUser,
        "InReview"       => Icons.Material.Filled.RateReview,
        "Provisional"    => Icons.Material.Filled.EditNote,
        "Rejected"       => Icons.Material.Filled.Block,
        "AwaitingStage2" => Icons.Material.Filled.HourglassTop,
        // Legacy
        "Review"      => Icons.Material.Filled.RateReview,
        "Auto"        => Icons.Material.Filled.CheckCircle,
        "Edited"      => Icons.Material.Filled.CheckCircle,
        "Staging"     => Icons.Material.Filled.HourglassBottom,
        "Duplicate"   => Icons.Material.Filled.FileCopy,
        _             => Icons.Material.Filled.HelpOutline,
    };

    /// <summary>Returns the display label for a registry status.</summary>
    public static string GetStatusLabel(string status) => status switch
    {
        "Identified"     => "Identified",
        "Confirmed"      => "Confirmed",
        "InReview"       => "In Review",
        "Provisional"    => "Provisional",
        "Rejected"       => "Rejected",
        "Known"          => "Known",
        "AwaitingStage2" => "Awaiting Wikidata",
        // Legacy
        "Review"      => "In Review",
        "Auto"        => "Identified",
        "Edited"      => "Identified",
        "Staging"     => "In Review",
        "Duplicate"   => "In Review",
        _             => status,
    };

    // ── Four-State Colors (Registry Overhaul spec) ────────────────────────

    /// <summary>Returns the hex color for the four-state model.</summary>
    public static string GetStateColor(string state)
    {
        var p = PaletteProvider.Current.Status;
        return state switch
        {
            "Identified"                          => p.Identified,
            "InReview" or "Review" or "NeedsReview" => p.InReview,
            "Provisional" or "NoMatch"            => p.Provisional,
            "Rejected" or "Failed"                => p.Rejected,
            "Known"                               => p.Known,
            _                                     => p.Provisional,
        };
    }

    /// <summary>Returns a CSS style for a state-colored left border (3px solid).</summary>
    public static string GetStateBorderStyle(string state) =>
        $"border-left: 3px solid {GetStateColor(state)};";

    /// <summary>Returns the label text for a four-state badge.</summary>
    public static string GetStateLabel(string state) => state switch
    {
        "Identified"               => "Identified",
        "InReview" or "Review"
            or "NeedsReview"        => "In Review",
        "Provisional" or "NoMatch"  => "Provisional",
        "Rejected" or "Failed"      => "Rejected",
        "Known"                    => "Known",
        _ => state,
    };

    /// <summary>Returns the icon for a four-state badge.</summary>
    public static string GetStateIcon(string state) => state switch
    {
        "Identified"               => Icons.Material.Outlined.CheckCircle,
        "InReview" or "Review"
            or "NeedsReview"        => Icons.Material.Outlined.RateReview,
        "Provisional" or "NoMatch"  => Icons.Material.Outlined.HelpOutline,
        "Rejected" or "Failed"      => Icons.Material.Outlined.Error,
        "Known"                    => Icons.Material.Outlined.Verified,
        _ => Icons.Material.Outlined.Circle,
    };

    // ── Timeline Event Helpers ──────────────────────────────────────────────

    /// <summary>
    /// Formats a day group label for the Timeline view: "TODAY", "YESTERDAY", or day name.
    /// </summary>
    public static string FormatTimelineDayGroup(DateTimeOffset timestamp)
    {
        var local = timestamp.ToLocalTime();
        var today = DateTimeOffset.Now.Date;

        if (local.Date == today) return "TODAY";
        if (local.Date == today.AddDays(-1)) return "YESTERDAY";
        if ((today - local.Date).TotalDays < 7) return local.ToString("dddd").ToUpperInvariant();
        return local.ToString("MMMM d").ToUpperInvariant();
    }

    // ── Batch Friendly Timestamps ──────────────────────────────────────────

    /// <summary>
    /// Formats a batch timestamp into a human-friendly label per the Registry spec:
    /// "Just now", "This afternoon", "Yesterday evening", "Tuesday, 3:42 PM", "March 18, 3:42 PM".
    /// </summary>
    public static string FormatBatchTimestamp(DateTimeOffset timestamp)
    {
        var now = DateTimeOffset.Now;
        var local = timestamp.ToLocalTime();
        var diff = now - local;

        // Within the last 2 minutes
        if (diff.TotalMinutes < 2)
            return "Just now";

        // Today
        if (local.Date == now.Date)
        {
            var period = GetDayPeriod(local);
            return $"This {period}";
        }

        // Yesterday
        if (local.Date == now.Date.AddDays(-1))
        {
            var period = GetDayPeriod(local);
            return $"Yesterday {period}";
        }

        // Within past 7 days — use day name
        if (diff.TotalDays < 7)
            return $"{local:dddd}, {local:h:mm tt}";

        // Older — use month + day
        return $"{local:MMMM d}, {local:h:mm tt}";
    }

    /// <summary>
    /// Formats a batch subtitle: "{count} items added to {category}" or "{count} items from watch folder".
    /// </summary>
    public static string FormatBatchSubtitle(int filesTotal, string? category, string? sourcePath)
    {
        var countText = filesTotal == 1 ? "1 item" : $"{filesTotal:N0} items";
        if (!string.IsNullOrWhiteSpace(category))
            return $"{countText} added to {category}";
        if (!string.IsNullOrWhiteSpace(sourcePath))
            return $"{countText} from watch folder";
        return $"{countText} processed";
    }

    /// <summary>
    /// Formats a batch processing duration into a compact string: "14m", "2h 5m", "45s".
    /// </summary>
    public static string FormatBatchDuration(DateTimeOffset started, DateTimeOffset? completed)
    {
        if (completed is null) return "in progress";
        var span = completed.Value - started;
        return span switch
        {
            { TotalSeconds: < 60 } => $"{(int)span.TotalSeconds}s",
            { TotalMinutes: < 60 } => $"{(int)span.TotalMinutes}m",
            _ => $"{(int)span.TotalHours}h {span.Minutes}m"
        };
    }

    private static string GetDayPeriod(DateTimeOffset dt) => dt.Hour switch
    {
        < 12 => "morning",
        < 17 => "afternoon",
        _ => "evening"
    };
}
