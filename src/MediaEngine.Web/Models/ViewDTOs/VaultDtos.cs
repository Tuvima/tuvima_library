namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>Vault pipeline stage states.</summary>
public enum VaultStageState { Completed, Warning, Failed, Pending, Running }

/// <summary>Vault item display statuses.</summary>
public enum VaultStatus { Verified, Provisional, NeedsReview, Quarantined, WaitingForProvider, Unowned, RetailMatched }

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
    public string PipelineStep { get; init; } = "Retail";
    public string VaultVisibility { get; init; } = "hidden";
    public bool IsReadyForVault { get; init; }
    public string ArtworkState { get; init; } = "pending";
    public string? ArtworkSource { get; init; }
    public DateTimeOffset? ArtworkSettledAt { get; init; }

    /// <summary>Name of the provider that is currently unreachable (if any).</summary>
    public string? FailedProviderName { get; init; }

    /// <summary>Artist headshot URL, set only for Music By Artist container rows.</summary>
    public string? ArtistPhotoUrl { get; init; }

    public VaultStatus VaultDisplayStatus => ComputeVaultStatus();
    public VaultPipelineStage Stage1 => ComputeRetailStage();
    public VaultPipelineStage Stage2 => ComputeWikidataStage();
    public VaultPipelineStage Stage3 => ComputeEnrichmentStage();

    // Legacy compact views still use these combined stages.
    public VaultPipelineStage IdentifiedStage => ComputeIdentifiedStage();
    public VaultPipelineStage EnrichedStage => ComputeEnrichedStage();

    public int ConfidenceSegments => (int)Math.Round(Confidence * 5);
    public string? UniverseName { get; init; }
    public int? QuarantineDays { get; init; }
    public string ReadinessLabel => ComputeReadinessLabel();

    /// <summary>
    /// Compact provenance summary for the Resolution column (e.g. "ISBN -> Q83471", "Title search -> Q12345").
    /// Returns null when no resolution has occurred.
    /// </summary>
    public string? ResolutionSummary => ComputeResolutionSummary();

    /// <summary>
    /// Human-readable format specs: "4K REMUX" for video, "FLAC" for audio,
    /// "EPUB" for books, etc. Computed from media type and available metadata.
    /// </summary>
    public string? Specs => ComputeSpecs();

    /// <summary>
    /// Primary creator for display: Director (Movies/TV), Author (Books/Audiobooks/Comics),
    /// Artist (Music). Falls through in priority order.
    /// </summary>
    public string? Creator =>
        !string.IsNullOrWhiteSpace(Director) ? Director :
        !string.IsNullOrWhiteSpace(Author) ? Author :
        !string.IsNullOrWhiteSpace(Artist) ? Artist :
        !string.IsNullOrWhiteSpace(Narrator) ? Narrator :
        null;

    /// <summary>
    /// Contextual grouping label: Show name (TV), Album (Music), Series (Books/Comics/Audiobooks).
    /// Used in the "Context" column of the redesigned media list.
    /// </summary>
    public string? ContextLabel =>
        !string.IsNullOrWhiteSpace(ShowName) ? ShowName :
        !string.IsNullOrWhiteSpace(Album) ? Album :
        !string.IsNullOrWhiteSpace(Series) ? Series :
        null;

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
        PipelineStep = r.PipelineStep,
        VaultVisibility = r.VaultVisibility,
        IsReadyForVault = r.IsReadyForVault,
        ArtworkState = r.ArtworkState,
        ArtworkSource = r.ArtworkSource,
        ArtworkSettledAt = r.ArtworkSettledAt,
        FailedProviderName = r.FailedProviderName,
    };

    private VaultStatus ComputeVaultStatus()
    {
        if (string.Equals(Status, "WaitingForProvider", StringComparison.OrdinalIgnoreCase))
            return VaultStatus.WaitingForProvider;

        if (string.Equals(Status, "Rejected", StringComparison.OrdinalIgnoreCase))
            return VaultStatus.Quarantined;

        if (NeedsReview())
            return VaultStatus.NeedsReview;

        if (string.Equals(Status, "RetailMatched", StringComparison.OrdinalIgnoreCase))
            return VaultStatus.RetailMatched;

        if (string.Equals(Status, "Provisional", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Status, "AwaitingStage2", StringComparison.OrdinalIgnoreCase)
            || string.Equals(VaultVisibility, "hidden", StringComparison.OrdinalIgnoreCase))
            return VaultStatus.Provisional;

        if (HasValidWikidataQid() || IsReadyForVault || HasUserLocks)
            return VaultStatus.Verified;

        return VaultStatus.Provisional;
    }

    private string? ComputeResolutionSummary()
    {
        if (!HasValidWikidataQid())
            return null;

        if (!string.IsNullOrEmpty(RetailMatchDetail) && RetailMatchDetail.Contains("ISBN", StringComparison.OrdinalIgnoreCase))
            return $"ISBN -> {WikidataQid}";
        if (!string.IsNullOrEmpty(RetailMatchDetail) && RetailMatchDetail.Contains("TMDB", StringComparison.OrdinalIgnoreCase))
            return $"TMDB -> {WikidataQid}";
        if (!string.IsNullOrEmpty(RetailMatchDetail) && RetailMatchDetail.Contains("ASIN", StringComparison.OrdinalIgnoreCase))
            return $"ASIN -> {WikidataQid}";
        if (HasRetailMatch())
            return $"{RetailMatch} -> {WikidataQid}";

        return $"Title search -> {WikidataQid}";
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

                    if (fn.Contains("2160P") || fn.Contains("4K") || fn.Contains("UHD"))
                        parts.Add("4K");
                    else if (fn.Contains("1080P") || fn.Contains("1080I"))
                        parts.Add("1080p");
                    else if (fn.Contains("720P"))
                        parts.Add("720p");
                    else if (fn.Contains("480P") || fn.Contains("SD"))
                        parts.Add("SD");

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
        if (IsRetailFailure())
            return new VaultPipelineStage { State = VaultStageState.Failed, Label = "Retail: No match" };

        if (IsRetailReview())
            return new VaultPipelineStage { State = VaultStageState.Warning, Label = "Retail: Needs review" };

        if (HasRetailMatch())
        {
            var label = !string.IsNullOrWhiteSpace(RetailMatchDetail)
                ? $"Retail: {RetailMatchDetail}"
                : "Retail: Matched";
            return new VaultPipelineStage { State = VaultStageState.Completed, Label = label };
        }

        if (string.Equals(Status, "WaitingForProvider", StringComparison.OrdinalIgnoreCase)
            || string.Equals(PipelineStep, "Retail", StringComparison.OrdinalIgnoreCase))
            return new VaultPipelineStage { State = VaultStageState.Running, Label = "Retail: In progress" };

        return new VaultPipelineStage { State = VaultStageState.Pending, Label = "Retail: Pending" };
    }

    private VaultPipelineStage ComputeWikidataStage()
    {
        if (HasValidWikidataQid())
            return new VaultPipelineStage { State = VaultStageState.Completed, Label = $"Wikidata: {WikidataQid}" };

        if (IsQidNoMatch())
            return new VaultPipelineStage { State = VaultStageState.Warning, Label = "Wikidata: No match" };

        if (IsWikidataReview())
            return new VaultPipelineStage { State = VaultStageState.Warning, Label = "Wikidata: Needs review" };

        if (HasRetailMatch()
            && (string.Equals(PipelineStep, "Wikidata", StringComparison.OrdinalIgnoreCase)
                || string.Equals(PipelineStep, "Enrichment", StringComparison.OrdinalIgnoreCase)
                || string.Equals(PipelineStep, "Complete", StringComparison.OrdinalIgnoreCase)))
            return new VaultPipelineStage { State = VaultStageState.Running, Label = "Wikidata: Resolving" };

        return new VaultPipelineStage { State = VaultStageState.Pending, Label = "Wikidata: Pending" };
    }

    private VaultPipelineStage ComputeEnrichmentStage()
    {
        if (string.Equals(ArtworkState, "present", StringComparison.OrdinalIgnoreCase))
        {
            var label = !string.IsNullOrEmpty(UniverseName)
                ? $"Enrichment: {UniverseName}"
                : "Enrichment: Artwork ready";
            return new VaultPipelineStage { State = VaultStageState.Completed, Label = label };
        }

        if (string.Equals(ArtworkState, "missing", StringComparison.OrdinalIgnoreCase))
            return new VaultPipelineStage { State = VaultStageState.Warning, Label = "Enrichment: No artwork found" };

        if (HasRetailMatch()
            || HasValidWikidataQid()
            || string.Equals(PipelineStep, "Enrichment", StringComparison.OrdinalIgnoreCase)
            || string.Equals(PipelineStep, "Complete", StringComparison.OrdinalIgnoreCase))
            return new VaultPipelineStage { State = VaultStageState.Running, Label = "Enrichment: Pending artwork" };

        return new VaultPipelineStage { State = VaultStageState.Pending, Label = "Enrichment: Pending" };
    }

    private VaultPipelineStage ComputeIdentifiedStage()
    {
        var retail = ComputeRetailStage();
        var wikidata = ComputeWikidataStage();

        if (retail.State == VaultStageState.Failed || wikidata.State == VaultStageState.Failed)
        {
            var failedLabel = retail.State == VaultStageState.Failed ? retail.Label : wikidata.Label;
            return new VaultPipelineStage { State = VaultStageState.Failed, Label = failedLabel };
        }

        if (wikidata.State == VaultStageState.Completed)
            return new VaultPipelineStage { State = VaultStageState.Completed, Label = wikidata.Label };

        if (retail.State == VaultStageState.Completed
            && (wikidata.State == VaultStageState.Pending || wikidata.State == VaultStageState.Running))
            return new VaultPipelineStage { State = VaultStageState.Running, Label = "Retail matched -> awaiting Wikidata" };

        if (retail.State == VaultStageState.Warning || wikidata.State == VaultStageState.Warning)
        {
            var warnLabel = retail.State == VaultStageState.Warning ? retail.Label : wikidata.Label;
            return new VaultPipelineStage { State = VaultStageState.Warning, Label = warnLabel };
        }

        return new VaultPipelineStage { State = VaultStageState.Pending, Label = "Not yet identified" };
    }

    private VaultPipelineStage ComputeEnrichedStage()
    {
        var enrichment = ComputeEnrichmentStage();
        return enrichment.State switch
        {
            VaultStageState.Completed => new VaultPipelineStage { State = VaultStageState.Completed, Label = enrichment.Label },
            VaultStageState.Running => new VaultPipelineStage { State = VaultStageState.Running, Label = "Enrichment in progress" },
            VaultStageState.Warning => new VaultPipelineStage { State = VaultStageState.Warning, Label = enrichment.Label },
            VaultStageState.Failed => new VaultPipelineStage { State = VaultStageState.Failed, Label = enrichment.Label },
            _ => new VaultPipelineStage { State = VaultStageState.Pending, Label = "Enrichment pending" },
        };
    }

    private bool HasValidWikidataQid() =>
        !string.IsNullOrEmpty(WikidataQid)
        && !WikidataQid.StartsWith("NF", StringComparison.OrdinalIgnoreCase);

    private bool HasRetailMatch() =>
        string.Equals(RetailMatch, "matched", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Status, "RetailMatched", StringComparison.OrdinalIgnoreCase)
        || HasValidWikidataQid()
        || IsQidNoMatch();

    private bool IsRetailFailure() =>
        string.Equals(RetailMatch, "failed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ReviewTrigger, "RetailMatchFailed", StringComparison.OrdinalIgnoreCase);

    private bool IsRetailReview() =>
        ReviewItemId.HasValue
        && ReviewTrigger is "AuthorityMatchFailed" or "RetailMatchFailed" or "ContentMatchFailed";

    private bool IsWikidataReview() =>
        ReviewItemId.HasValue
        && ReviewTrigger is "MissingQid" or "MultipleQidMatches" or "WikidataBridgeFailed";

    private bool IsQidNoMatch() =>
        string.Equals(Status, "QidNoMatch", StringComparison.OrdinalIgnoreCase)
        || string.Equals(WikidataMatch, "failed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(WikidataStatus, "missing", StringComparison.OrdinalIgnoreCase);

    private bool NeedsReview() =>
        ReviewItemId.HasValue
        || string.Equals(Status, "InReview", StringComparison.OrdinalIgnoreCase)
        || string.Equals(VaultVisibility, "review_only", StringComparison.OrdinalIgnoreCase);

    private string ComputeReadinessLabel()
    {
        if (NeedsReview())
            return "Needs review";

        if (string.Equals(ArtworkState, "pending", StringComparison.OrdinalIgnoreCase))
            return "Pending artwork";

        if (IsReadyForVault && string.Equals(VaultVisibility, "visible", StringComparison.OrdinalIgnoreCase))
            return "Ready";

        if (string.Equals(VaultVisibility, "hidden", StringComparison.OrdinalIgnoreCase))
            return "Hidden";

        return "Pending";
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
