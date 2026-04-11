namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>Vault pipeline stage states.</summary>
public enum VaultStageState { Completed, Warning, Failed, Pending, Running }

/// <summary>Vault item display statuses.</summary>
public enum VaultStatus { Verified, Provisional, NeedsReview, Quarantined, WaitingForProvider, Unowned }

/// <summary>A single pipeline stage indicator.</summary>
public sealed class VaultPipelineStage
{
    public VaultStageState State { get; init; }
    public string Label { get; init; } = "";
}

/// <summary>Wraps RegistryItemViewModel with Vault-specific computed properties.</summary>
public sealed class VaultItemViewModel
{
    public Guid EntityId { get; init; }
    public string Title { get; init; } = "";
    public string? OriginalTitle { get; init; }
    public string? Author { get; init; }
    public string? Director { get; init; }
    public string? Artist { get; init; }
    public string? Series { get; init; }
    public string? SeriesPosition { get; init; }
    public string? Narrator { get; init; }
    public string? Genre { get; init; }
    public string? Runtime { get; init; }
    public string? Rating { get; init; }
    public string? Album { get; init; }
    public string? TrackNumber { get; init; }
    public string? Season { get; init; }
    public string? Episode { get; init; }
    public string? Duration { get; init; }
    public string? DiscNumber { get; init; }
    public string? ShowName { get; init; }
    public string? EpisodeTitle { get; init; }
    public string? Network { get; init; }
    public string? TopCast { get; init; }
    public string? TrackCount { get; init; }
    public string? SeasonCount { get; init; }
    public string? AlbumCount { get; init; }
    public string? Year { get; init; }
    public string MediaType { get; init; } = "";
    public string? CoverUrl { get; init; }
    public string? CoverThumbUrl { get; init; }
    public double Confidence { get; init; }
    public string Status { get; init; } = "";
    public string? ReviewTrigger { get; init; }
    public string? WikidataQid { get; init; }
    public string? WikidataStatus { get; init; }
    public string RetailMatch { get; init; } = "none";
    public string? RetailMatchDetail { get; init; }
    public string WikidataMatch { get; init; } = "none";
    public DateTimeOffset CreatedAt { get; init; }
    public string? FileName { get; init; }
    public string? FilePath { get; init; }
    public long? FileSizeBytes { get; init; }
    public Guid? ReviewItemId { get; init; }
    public bool HasUserLocks { get; init; }
    public string? HeroUrl { get; init; }
    public bool MissingUniverse { get; init; }

    /// <summary>Name of the provider that is currently unreachable (if any).</summary>
    public string? FailedProviderName { get; init; }

    /// <summary>Artist headshot URL — set only for Music By Artist container rows.</summary>
    public string? ArtistPhotoUrl { get; init; }

    // Computed: the 4 vault display statuses
    public VaultStatus VaultDisplayStatus => ComputeVaultStatus();

    // Computed: the 4 pipeline stages (Stage0 = File)
    public VaultPipelineStage Stage0 => ComputeFileStage();
    public VaultPipelineStage Stage1 => ComputeRetailStage();
    public VaultPipelineStage Stage2 => ComputeWikidataStage();
    public VaultPipelineStage Stage3 => ComputeUniverseStage();

    // Computed: confidence segments (0-5) for the 5-bar indicator
    public int ConfidenceSegments => (int)Math.Round(Confidence * 5);

    // Placeholder for universe/hub name
    public string? UniverseName { get; init; }

    // Quarantine days remaining (for rejected items, placeholder)
    public int? QuarantineDays { get; init; }

    /// <summary>
    /// Compact provenance summary for the Resolution column (e.g. "ISBN → Q83471", "Title search → Q12345").
    /// Returns null when no resolution has occurred.
    /// </summary>
    public string? ResolutionSummary => ComputeResolutionSummary();

    /// <summary>
    /// Human-readable format specs: "4K REMUX" for video, "FLAC 24-bit" for audio,
    /// "EPUB" for books, etc. Computed from media type and available metadata.
    /// </summary>
    public string? Specs => ComputeSpecs();

    /// <summary>Factory: convert a RegistryItemViewModel to VaultItemViewModel.</summary>
    public static VaultItemViewModel From(RegistryItemViewModel r) => new()
    {
        EntityId = r.EntityId,
        Title = r.Title,
        OriginalTitle = r.OriginalTitle,
        Author = r.Author,
        Director = r.Director,
        Artist = r.Artist,
        Series = r.Series,
        SeriesPosition = r.SeriesPosition,
        Narrator = r.Narrator,
        Genre = r.Genre,
        Runtime = r.Runtime,
        Rating = r.Rating,
        Album = r.Album,
        TrackNumber = r.TrackNumber,
        Season = r.Season,
        Episode = r.Episode,
        Duration = r.Duration,
        DiscNumber = r.DiscNumber,
        ShowName = r.ShowName,
        EpisodeTitle = r.EpisodeTitle,
        Network = r.Network,
        TopCast = r.TopCast,
        TrackCount = r.TrackCount,
        Year = r.Year,
        MediaType = r.MediaType,
        CoverUrl = r.CoverUrl,
        CoverThumbUrl = !string.IsNullOrEmpty(r.CoverUrl)
            ? r.CoverUrl.Replace("/cover", "/cover-thumb")
            : null,
        Confidence = r.Confidence,
        Status = r.Status,
        ReviewTrigger = r.ReviewTrigger,
        WikidataQid = r.WikidataQid,
        WikidataStatus = r.WikidataStatus,
        RetailMatch = r.RetailMatch,
        RetailMatchDetail = r.RetailMatchDetail,
        WikidataMatch = r.WikidataMatch,
        CreatedAt = r.CreatedAt,
        FileName = r.FileName,
        FilePath = r.FilePath,
        FileSizeBytes = r.FileSizeBytes,
        ReviewItemId = r.ReviewItemId,
        HasUserLocks = r.HasUserLocks,
        HeroUrl = r.HeroUrl,
        MissingUniverse = r.MissingUniverse,
        FailedProviderName = r.FailedProviderName,
    };

    private VaultStatus ComputeVaultStatus()
    {
        // WaitingForProvider — provider is unreachable, will retry automatically
        if (string.Equals(Status, "WaitingForProvider", StringComparison.OrdinalIgnoreCase))
            return VaultStatus.WaitingForProvider;

        // Rejected → Quarantined
        if (string.Equals(Status, "Rejected", StringComparison.OrdinalIgnoreCase))
            return VaultStatus.Quarantined;

        // Has a failed-type review trigger → NeedsReview
        if (!string.IsNullOrEmpty(ReviewTrigger) && ReviewTrigger is
            "AuthorityMatchFailed" or "ContentMatchFailed" or "StagedUnidentifiable"
            or "PlaceholderTitle" or "WikidataBridgeFailed" or "RetailMatchFailed")
            return VaultStatus.NeedsReview;

        // Provisional or AwaitingStage2 without failed triggers → Provisional
        if (string.Equals(Status, "Provisional", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Status, "AwaitingStage2", StringComparison.OrdinalIgnoreCase))
            return VaultStatus.Provisional;

        // Any other review trigger or InReview status → Needs Review
        if (!string.IsNullOrEmpty(ReviewTrigger)
            || string.Equals(Status, "InReview", StringComparison.OrdinalIgnoreCase))
            return VaultStatus.NeedsReview;

        // Identified/Confirmed with QID → Verified
        if (!string.IsNullOrEmpty(WikidataQid)
            && !WikidataQid.StartsWith("NF", StringComparison.OrdinalIgnoreCase))
            return VaultStatus.Verified;

        // Identified without QID but has user locks → Verified
        if (HasUserLocks)
            return VaultStatus.Verified;

        // Default: Needs Review
        return VaultStatus.NeedsReview;
    }

    private VaultPipelineStage ComputeFileStage()
    {
        // If the item exists in the vault the file was successfully scanned — always Completed.
        return new VaultPipelineStage { State = VaultStageState.Completed, Label = "File" };
    }

    private string? ComputeResolutionSummary()
    {
        // Wikidata resolved via a bridge ID (e.g. ISBN → Q83471)
        if (!string.IsNullOrEmpty(WikidataQid)
            && !WikidataQid.StartsWith("NF", StringComparison.OrdinalIgnoreCase))
        {
            // If we know the retail match provider, prefix with that
            if (!string.IsNullOrEmpty(RetailMatchDetail) && RetailMatchDetail.Contains("ISBN", StringComparison.OrdinalIgnoreCase))
                return $"ISBN \u2192 {WikidataQid}";
            if (!string.IsNullOrEmpty(RetailMatchDetail) && RetailMatchDetail.Contains("TMDB", StringComparison.OrdinalIgnoreCase))
                return $"TMDB \u2192 {WikidataQid}";
            if (!string.IsNullOrEmpty(RetailMatchDetail) && RetailMatchDetail.Contains("ASIN", StringComparison.OrdinalIgnoreCase))
                return $"ASIN \u2192 {WikidataQid}";
            if (!string.IsNullOrEmpty(RetailMatch)
                && RetailMatch != "none"
                && RetailMatch != "failed"
                && !RetailMatch.Equals("local_processor", StringComparison.OrdinalIgnoreCase)
                && !RetailMatch.Equals("library_scanner", StringComparison.OrdinalIgnoreCase))
                return $"{RetailMatch} \u2192 {WikidataQid}";
            return $"Title search \u2192 {WikidataQid}";
        }

        return null;
    }

    private string? ComputeSpecs()
    {
        var type = MediaType?.ToLowerInvariant();

        switch (type)
        {
            case "movies" or "tv":
                if (!string.IsNullOrEmpty(FileName))
                {
                    var fn = FileName.ToUpperInvariant();
                    var parts = new List<string>();

                    // Resolution
                    if (fn.Contains("2160P") || fn.Contains("4K") || fn.Contains("UHD"))
                        parts.Add("4K");
                    else if (fn.Contains("1080P") || fn.Contains("1080I"))
                        parts.Add("1080p");
                    else if (fn.Contains("720P"))
                        parts.Add("720p");
                    else if (fn.Contains("480P") || fn.Contains("SD"))
                        parts.Add("SD");

                    // Source/quality
                    if (fn.Contains("REMUX"))
                        parts.Add("REMUX");
                    else if (fn.Contains("BLURAY") || fn.Contains("BLU-RAY"))
                        parts.Add("Blu-ray");
                    else if (fn.Contains("WEBDL") || fn.Contains("WEB-DL"))
                        parts.Add("WEB-DL");
                    else if (fn.Contains("WEBRIP") || fn.Contains("WEB-RIP"))
                        parts.Add("WEBRip");
                    else if (fn.Contains("HDTV"))
                        parts.Add("HDTV");

                    // Codec
                    if (fn.Contains("HEVC") || fn.Contains("X265") || fn.Contains("H265") || fn.Contains("H.265"))
                        parts.Add("HEVC");
                    else if (fn.Contains("X264") || fn.Contains("H264") || fn.Contains("H.264") || fn.Contains("AVC"))
                        parts.Add("H.264");

                    if (parts.Count > 0)
                        return string.Join(" ", parts);
                }
                return null;

            case "music":
                if (!string.IsNullOrEmpty(FileName))
                {
                    var ext = Path.GetExtension(FileName).ToUpperInvariant();
                    return ext switch
                    {
                        ".FLAC" => "FLAC",
                        ".ALAC" or ".M4A" => "ALAC",
                        ".MP3" => "MP3",
                        ".WAV" => "WAV",
                        ".OGG" => "OGG",
                        ".AAC" => "AAC",
                        ".OPUS" => "Opus",
                        _ => ext.TrimStart('.'),
                    };
                }
                return null;

            case "audiobooks":
                if (!string.IsNullOrEmpty(FileName))
                {
                    var ext = Path.GetExtension(FileName).ToUpperInvariant();
                    return ext switch
                    {
                        ".M4B" => "M4B",
                        ".MP3" => "MP3",
                        ".M4A" => "M4A",
                        _ => ext.TrimStart('.'),
                    };
                }
                return null;

            case "books":
                if (!string.IsNullOrEmpty(FileName))
                {
                    var ext = Path.GetExtension(FileName).ToUpperInvariant();
                    return ext switch
                    {
                        ".EPUB" => "EPUB",
                        ".PDF" => "PDF",
                        ".MOBI" => "MOBI",
                        ".AZW3" => "AZW3",
                        ".CBZ" => "CBZ",
                        _ => ext.TrimStart('.'),
                    };
                }
                return null;

            case "comics":
                if (!string.IsNullOrEmpty(FileName))
                {
                    var ext = Path.GetExtension(FileName).ToUpperInvariant();
                    return ext switch
                    {
                        ".CBZ" => "CBZ",
                        ".CBR" => "CBR",
                        ".PDF" => "PDF",
                        _ => ext.TrimStart('.'),
                    };
                }
                return null;

            default:
                return null;
        }
    }

    private VaultPipelineStage ComputeRetailStage()
    {
        if (string.Equals(RetailMatch, "failed", StringComparison.OrdinalIgnoreCase))
            return new VaultPipelineStage { State = VaultStageState.Failed, Label = "Retail: No Match" };
        if (RetailMatch is not "none" and not "" and not null)
        {
            // File scanner / local processor is NOT a retail match — show as Pending/Unmatched
            if (RetailMatch.Equals("local_processor", StringComparison.OrdinalIgnoreCase)
                || RetailMatch.Equals("library_scanner", StringComparison.OrdinalIgnoreCase))
                return new VaultPipelineStage { State = VaultStageState.Pending, Label = "Unmatched" };

            var label = !string.IsNullOrWhiteSpace(RetailMatchDetail)
                ? $"Retail: {RetailMatchDetail}"
                : $"Retail: {RetailMatch}";
            return new VaultPipelineStage { State = VaultStageState.Completed, Label = label };
        }
        return new VaultPipelineStage { State = VaultStageState.Pending, Label = "Retail: Pending" };
    }

    private VaultPipelineStage ComputeWikidataStage()
    {
        if (string.Equals(WikidataMatch, "failed", StringComparison.OrdinalIgnoreCase))
            return new VaultPipelineStage { State = VaultStageState.Failed, Label = "Wikidata: No Match" };
        if (!string.IsNullOrEmpty(WikidataQid) && !WikidataQid.StartsWith("NF", StringComparison.OrdinalIgnoreCase))
            return new VaultPipelineStage { State = VaultStageState.Completed, Label = $"Wikidata: {WikidataQid}" };
        if (RetailMatch is not "none" and not "" and not null and not "failed")
            return new VaultPipelineStage { State = VaultStageState.Warning, Label = "Wikidata: Awaiting" };
        return new VaultPipelineStage { State = VaultStageState.Pending, Label = "Wikidata: Pending" };
    }

    private VaultPipelineStage ComputeUniverseStage()
    {
        if (!string.IsNullOrEmpty(UniverseName))
            return new VaultPipelineStage { State = VaultStageState.Completed, Label = $"Universe: {UniverseName}" };
        if (!string.IsNullOrEmpty(WikidataQid)
            && !WikidataQid.StartsWith("NF", StringComparison.OrdinalIgnoreCase)
            && !MissingUniverse)
            return new VaultPipelineStage { State = VaultStageState.Completed, Label = "Universe: Mapped" };
        return new VaultPipelineStage { State = VaultStageState.Pending, Label = "Universe: Pending" };
    }
}

/// <summary>Filter state for the Vault toolbar.</summary>
public sealed class VaultFilterState
{
    public string? SearchText { get; set; }
    public string SortBy { get; set; } = "newest";
    public string GroupBy { get; set; } = "none";
    public string ViewMode { get; set; } = "list";
    public string? MediaTypeFilter { get; set; }
    public string? StatusFilter { get; set; }
}
