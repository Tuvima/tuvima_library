namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>Vault pipeline stage states.</summary>
public enum VaultStageState { Completed, Warning, Failed, Pending }

/// <summary>Vault item display statuses.</summary>
public enum VaultStatus { Verified, NeedsReview, Failed, Quarantined }

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
    public string? Author { get; init; }
    public string? Year { get; init; }
    public string MediaType { get; init; } = "";
    public string? CoverUrl { get; init; }
    public double Confidence { get; init; }
    public string Status { get; init; } = "";
    public string? ReviewTrigger { get; init; }
    public string? WikidataQid { get; init; }
    public string? WikidataStatus { get; init; }
    public string RetailMatch { get; init; } = "none";
    public string WikidataMatch { get; init; } = "none";
    public DateTimeOffset CreatedAt { get; init; }
    public string? FileName { get; init; }
    public long? FileSizeBytes { get; init; }
    public Guid? ReviewItemId { get; init; }
    public bool HasUserLocks { get; init; }
    public string? HeroUrl { get; init; }
    public bool MissingUniverse { get; init; }

    // Computed: the 4 vault display statuses
    public VaultStatus VaultDisplayStatus => ComputeVaultStatus();

    // Computed: the 3 pipeline stages
    public VaultPipelineStage Stage1 => ComputeRetailStage();
    public VaultPipelineStage Stage2 => ComputeWikidataStage();
    public VaultPipelineStage Stage3 => ComputeUniverseStage();

    // Computed: confidence segments (0-5) for the 5-bar indicator
    public int ConfidenceSegments => (int)Math.Round(Confidence * 5);

    // Placeholder for universe/hub name
    public string? UniverseName { get; init; }

    // Quarantine days remaining (for rejected items, placeholder)
    public int? QuarantineDays { get; init; }

    /// <summary>Factory: convert a RegistryItemViewModel to VaultItemViewModel.</summary>
    public static VaultItemViewModel From(RegistryItemViewModel r) => new()
    {
        EntityId = r.EntityId,
        Title = r.Title,
        Author = r.Author,
        Year = r.Year,
        MediaType = r.MediaType,
        CoverUrl = r.CoverUrl,
        Confidence = r.Confidence,
        Status = r.Status,
        ReviewTrigger = r.ReviewTrigger,
        WikidataQid = r.WikidataQid,
        WikidataStatus = r.WikidataStatus,
        RetailMatch = r.RetailMatch,
        WikidataMatch = r.WikidataMatch,
        CreatedAt = r.CreatedAt,
        FileName = r.FileName,
        FileSizeBytes = r.FileSizeBytes,
        ReviewItemId = r.ReviewItemId,
        HasUserLocks = r.HasUserLocks,
        HeroUrl = r.HeroUrl,
        MissingUniverse = r.MissingUniverse,
    };

    private VaultStatus ComputeVaultStatus()
    {
        // Rejected → Quarantined
        if (string.Equals(Status, "Rejected", StringComparison.OrdinalIgnoreCase))
            return VaultStatus.Quarantined;

        // Has a failed-type review trigger → Failed
        if (!string.IsNullOrEmpty(ReviewTrigger) && ReviewTrigger is
            "AuthorityMatchFailed" or "ContentMatchFailed" or "StagedUnidentifiable"
            or "PlaceholderTitle" or "WikidataBridgeFailed" or "RetailMatchFailed")
            return VaultStatus.Failed;

        // Any other review trigger or InReview/Provisional status → Needs Review
        if (!string.IsNullOrEmpty(ReviewTrigger)
            || string.Equals(Status, "InReview", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Status, "Provisional", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Status, "AwaitingStage2", StringComparison.OrdinalIgnoreCase))
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

    private VaultPipelineStage ComputeRetailStage()
    {
        if (string.Equals(RetailMatch, "failed", StringComparison.OrdinalIgnoreCase))
            return new VaultPipelineStage { State = VaultStageState.Failed, Label = "Retail Match Failed" };
        if (RetailMatch is not "none" and not "")
            return new VaultPipelineStage { State = VaultStageState.Completed, Label = "Retail Matched" };
        return new VaultPipelineStage { State = VaultStageState.Pending, Label = "Retail Pending" };
    }

    private VaultPipelineStage ComputeWikidataStage()
    {
        if (string.Equals(WikidataMatch, "failed", StringComparison.OrdinalIgnoreCase))
            return new VaultPipelineStage { State = VaultStageState.Failed, Label = "Wikidata Match Failed" };
        if (WikidataMatch is not "none" and not "")
            return new VaultPipelineStage { State = VaultStageState.Completed, Label = "Wikidata Linked" };
        // If retail succeeded but wikidata hasn't run yet → warning
        if (RetailMatch is not "none" and not "" and not "failed")
            return new VaultPipelineStage { State = VaultStageState.Warning, Label = "Wikidata Pending" };
        return new VaultPipelineStage { State = VaultStageState.Pending, Label = "Wikidata Pending" };
    }

    private VaultPipelineStage ComputeUniverseStage()
    {
        // Stub: Universe placement is not yet tracked per-item.
        // Show completed only if item has a QID and is not missing universe.
        if (!string.IsNullOrEmpty(WikidataQid)
            && !WikidataQid.StartsWith("NF", StringComparison.OrdinalIgnoreCase)
            && !MissingUniverse)
            return new VaultPipelineStage { State = VaultStageState.Completed, Label = "Universe Mapped" };

        return new VaultPipelineStage { State = VaultStageState.Pending, Label = "Universe Pending" };
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
