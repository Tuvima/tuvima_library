using MediaEngine.Domain;
using MediaEngine.Web.Components.Registry;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Theming;
using MudBlazor;

namespace MediaEngine.Web.Components.Vault;

/// <summary>Static display helpers for Vault components.</summary>
public static class VaultHelpers
{
    /// <summary>Returns the hex color for a VaultStatus.</summary>
    public static string GetVaultStatusColor(VaultStatus status)
    {
        var p = PaletteProvider.Current.Status;
        return status switch
        {
            VaultStatus.Verified    => p.Verified,
            VaultStatus.Provisional => p.Provisional,
            VaultStatus.NeedsReview => p.NeedsReview,
            VaultStatus.Quarantined => p.Quarantined,
            _                       => p.Default,
        };
    }

    /// <summary>Returns the display label for a VaultStatus.</summary>
    public static string GetVaultStatusLabel(VaultStatus status) => status switch
    {
        VaultStatus.Verified => "Verified",
        VaultStatus.Provisional => "Provisional",
        VaultStatus.NeedsReview => "Needs Review",
        VaultStatus.Quarantined => "Quarantined",
        _ => "Unknown",
    };

    /// <summary>Returns the icon for a VaultStatus.</summary>
    public static string GetVaultStatusIcon(VaultStatus status) => status switch
    {
        VaultStatus.Verified => Icons.Material.Filled.CheckCircle,
        VaultStatus.Provisional => Icons.Material.Filled.Info,
        VaultStatus.NeedsReview => Icons.Material.Filled.Warning,
        VaultStatus.Quarantined => Icons.Material.Filled.Block,
        _ => Icons.Material.Filled.HelpOutline,
    };

    /// <summary>Returns the hex color for a pipeline stage state.</summary>
    public static string GetStageColor(VaultStageState state)
    {
        var p = PaletteProvider.Current.Pipeline;
        return state switch
        {
            VaultStageState.Completed => p.Completed,
            VaultStageState.Warning   => p.Warning,
            VaultStageState.Failed    => p.Failed,
            VaultStageState.Pending   => p.Pending,
            _                         => p.Pending,
        };
    }

    /// <summary>Returns the CSS shadow glow for a pipeline stage state.</summary>
    public static string GetStageShadow(VaultStageState state)
    {
        var p = PaletteProvider.Current.Pipeline;
        return state switch
        {
            VaultStageState.Completed => $"0 0 8px {HexToRgba(p.Completed, 0.3)}",
            VaultStageState.Warning   => $"0 0 8px {HexToRgba(p.Warning, 0.3)}",
            VaultStageState.Failed    => $"0 0 8px {HexToRgba(p.Failed, 0.3)}",
            _                         => "none",
        };
    }

    /// <summary>Returns the confidence bar fill color based on score.</summary>
    /// <remarks>
    /// Thresholds: ≥0.85 = green (matches AutoLinkThreshold), ≥0.60 = amber (matches ConflictThreshold).
    /// TODO: drive thresholds from ScoringConfiguration once VaultHelpers is no longer static.
    /// </remarks>
    public static string GetConfidenceColor(double confidence)
    {
        var p = PaletteProvider.Current.Confidence;
        return confidence switch
        {
            >= 0.85 => p.High,
            >= 0.60 => p.Medium,
            _       => p.Low,
        };
    }

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
        "BOOK" or "BOOKS" or "EPUB" => ["Open Library"],
        "AUDIOBOOK" or "AUDIOBOOKS" => ["Apple API"],
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

    /// <summary>
    /// Returns a plain-language explanation of a review trigger for new users.
    /// Written from the perspective of someone who has never seen Tuvima before.
    /// </summary>
    public static string HumanizeReviewTrigger(string? trigger) => trigger switch
    {
        "RetailMatchFailed"      => "No provider could find a match for this file. You can try searching manually.",
        "WikidataBridgeFailed"   => "This item couldn't be linked to a known entry on Wikidata.",
        "LowConfidence"          => "The match confidence is too low to confirm automatically. Please review the suggested matches.",
        "MultipleQidMatches"     => "Multiple possible matches were found. Please pick the correct one.",
        "AuthorityMatchFailed"   => "The metadata authority couldn't verify this item's identity.",
        "ContentMatchFailed"     => "No content provider recognized this file.",
        "RetailMatchAmbiguous"   => "A possible match was found but it's not certain. Please verify.",
        "AmbiguousMediaType"     => "The file type is ambiguous — it could be music, an audiobook, or a podcast.",
        "MissingQid"             => "A retail match was found but it hasn't been linked to Wikidata yet.",
        "StagedUnidentifiable"   => "This file couldn't be identified. Check the filename and embedded metadata.",
        "PlaceholderTitle"       => "The title appears to be a placeholder (e.g. 'Unknown'). Please provide the real title.",
        "ArtworkUnconfirmed"     => "Cover art was found via text search and may not be accurate.",
        "LanguageMismatch"       => "This file's language doesn't match your library's configured language.",
        "UserReport"             => "You flagged this item for review.",
        "RootWatchFolder"        => "This file was placed in the root watch folder — its media type couldn't be determined.",
        "UserFixMatch"           => "You requested to fix the match for this item.",
        _ => "This item needs your attention.",
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
        [WellKnownProviders.LocalProcessor]  = "File Scan",
        [WellKnownProviders.LibraryScanner]  = "Library Scanner",
        [WellKnownProviders.AppleApi]        = "Apple API",
        [WellKnownProviders.Audnexus]        = "Audnexus",
        [WellKnownProviders.Wikidata]        = "Wikidata",
        [WellKnownProviders.Wikipedia]       = "Wikipedia",
        [WellKnownProviders.OpenLibrary]     = "Open Library",
        [WellKnownProviders.MusicBrainz]     = "MusicBrainz",
        [WellKnownProviders.Tmdb]            = "TMDB",
        [WellKnownProviders.Metron]          = "Metron",
        [WellKnownProviders.ApplePodcasts]   = "Apple Podcasts",
        [WellKnownProviders.PodcastIndex]    = "Podcast Index",
        [WellKnownProviders.AiProvider]      = "Fanart.tv",
        [WellKnownProviders.UserManual]      = "Manual Match",
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
            "musicbrainz"              => "MusicBrainz",
            "tmdb"                     => "TMDB",
            "comic_vine"               => "Comic Vine",
            "metron"                   => "Metron",
            "apple_podcasts"           => "Apple Podcasts",
            "podcast_index"            => "Podcast Index",
            "fanart_tv"                => "Fanart.tv",
            "local_filesystem"         => "File Scan",
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
    public static bool IsFileSource(Guid providerId) =>
        WellKnownProviders.IsFileSource(providerId);

    /// <summary>
    /// Returns true if the given provider GUID represents a user manual source.
    /// </summary>
    public static bool IsUserSource(Guid providerId) =>
        WellKnownProviders.IsUserSource(providerId);

    /// <summary>
    /// Builds a clickable external URL for a given bridge ID key and value.
    /// Returns null if no URL template is known for the key.
    /// </summary>
    public static (string Label, string Url)? BuildProviderUrl(string key, string value, string? mediaType = null)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        return key.ToLowerInvariant() switch
        {
            BridgeIdKeys.TmdbId when (mediaType ?? "").Contains("TV", StringComparison.OrdinalIgnoreCase)
                => ("View on TMDB", $"https://www.themoviedb.org/tv/{value}"),
            BridgeIdKeys.TmdbId
                => ("View on TMDB", $"https://www.themoviedb.org/movie/{value}"),
            BridgeIdKeys.OpenLibraryId or "olid"
                => ("View on Open Library", $"https://openlibrary.org/works/{value}"),
            BridgeIdKeys.MusicBrainzId
                => ("View on MusicBrainz", $"https://musicbrainz.org/release/{value}"),
            BridgeIdKeys.WikidataQid when value.StartsWith("Q", StringComparison.OrdinalIgnoreCase)
                => ("View on Wikidata", $"https://www.wikidata.org/wiki/{value}"),
            BridgeIdKeys.ImdbId when value.StartsWith("tt", StringComparison.OrdinalIgnoreCase)
                => ("View on IMDb", $"https://www.imdb.com/title/{value}"),
            BridgeIdKeys.AppleBooksId
                => ("View on Apple Books", $"https://books.apple.com/book/id{value}"),
            _ => null,
        };
    }

    /// <summary>Formats a claim key (snake_case field name) into a human-readable label.</summary>
    public static string FormatClaimKey(string key) => key.ToLowerInvariant() switch
    {
        "title"                     => "Title",
        "original_title"            => "Original Title",
        "author"                    => "Author",
        "director"                  => "Director",
        "artist"                    => "Artist",
        "narrator"                  => "Narrator",
        "year"                      => "Year",
        "genre"                     => "Genre",
        "series"                    => "Series",
        "series_position"           => "Series #",
        "description"               => "Description",
        "cover_url"                 => "Cover Art",
        "isbn"                      => "ISBN",
        "isbn_13"                   => "ISBN-13",
        "isbn_10"                   => "ISBN-10",
        "asin"                      => "ASIN",
        "tmdb_id"                   => "TMDB ID",
        "imdb_id"                   => "IMDb ID",
        "musicbrainz_id"            => "MusicBrainz ID",
        "open_library_id"           => "Open Library ID",
        "apple_books_id"            => "Apple Books ID",
        "apple_music_id"            => "Apple Music ID",
        "apple_music_collection_id" => "Apple Album ID",
        "apple_artist_id"           => "Apple Artist ID",
        "apple_podcasts_id"         => "Apple Podcasts ID",
        "comic_vine_id"             => "Comic Vine ID",
        "podcast_index_id"          => "Podcast Index ID",
        "wikidata_qid"              => "Wikidata QID",
        "show_name"                 => "Show Name",
        "episode_title"             => "Episode Title",
        "season_number"             => "Season",
        "episode_number"            => "Episode",
        "track_number"              => "Track #",
        "album"                     => "Album",
        "composer"                  => "Composer",
        "rating"                    => "Rating",
        "runtime"                   => "Runtime",
        "duration"                  => "Duration",
        "publisher"                 => "Publisher",
        "language"                  => "Language",
        "page_count"                => "Pages",
        "feed_url"                  => "Feed URL",
        "barcode"                   => "Barcode",
        "store_date"                => "Store Date",
        "explicit"                  => "Explicit",
        "media_type"                => "Media Type",
        "illustrator"               => "Illustrator",
        "screenwriter"              => "Screenwriter",
        "cast_member"               => "Actor",
        _                           => string.Join(' ', key.Split('_').Select(w =>
                                           w.Length > 0 ? char.ToUpperInvariant(w[0]) + w[1..] : w)),
    };

    /// <summary>Returns the brand colour for a media type string, matching stats bar colours.</summary>
    public static string GetMediaTypeColor(string? mediaType)
    {
        var p = PaletteProvider.Current.MediaType;
        var t = (mediaType ?? "").ToLowerInvariant();
        if (t.Contains("movie") || t.Contains("video")) return p.Movie;
        if (t.Contains("book") && !t.Contains("audio")) return p.Book;
        if (t.Contains("audiobook")) return p.Audiobook;
        if (t == "tv") return p.TV;
        if (t.Contains("music")) return p.Music;
        if (t.Contains("podcast")) return p.Podcast;
        if (t.Contains("comic")) return p.Comic;
        return p.Unknown;
    }
}
