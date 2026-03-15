namespace MediaEngine.Domain.Models;

/// <summary>Query parameters for paginated registry listing.</summary>
public sealed record RegistryQuery(
    int Offset = 0,
    int Limit = 50,
    string? Search = null,
    string? MediaType = null,
    string? Status = null,
    double? MinConfidence = null,
    string? MatchSource = null,
    bool DuplicatesOnly = false);

/// <summary>A single item in the registry listing.</summary>
public sealed record RegistryItem
{
    public Guid EntityId { get; init; }
    public string Title { get; init; } = "";
    public string? Year { get; init; }
    public string MediaType { get; init; } = "";
    public string? CoverUrl { get; init; }
    public string? MatchSource { get; init; }
    public string? MatchMethod { get; init; }
    public double Confidence { get; init; }
    public string Status { get; init; } = "Auto";
    public bool HasDuplicate { get; init; }
    public string? DuplicateOf { get; init; }
    public Guid? ReviewItemId { get; init; }
    public string? ReviewTrigger { get; init; }
    public bool HasUserLocks { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string? FileName { get; init; }
    public long? FileSizeBytes { get; init; }
    public string? Author { get; init; }
}

/// <summary>Paginated result from registry listing.</summary>
public sealed record RegistryPageResult(
    IReadOnlyList<RegistryItem> Items,
    int TotalCount,
    bool HasMore);

/// <summary>Detailed view of a single registry item for expanded row.</summary>
public sealed record RegistryItemDetail
{
    public Guid EntityId { get; init; }
    public string Title { get; init; } = "";
    public string? Year { get; init; }
    public string MediaType { get; init; } = "";
    public string? CoverUrl { get; init; }
    public double Confidence { get; init; }
    public string Status { get; init; } = "Auto";
    public string? MatchSource { get; init; }
    public string? MatchMethod { get; init; }

    // Metadata
    public string? Author { get; init; }
    public string? Director { get; init; }
    public string? Cast { get; init; }
    public string? Language { get; init; }
    public string? Genre { get; init; }
    public string? Runtime { get; init; }
    public string? Description { get; init; }
    public string? Series { get; init; }
    public string? SeriesPosition { get; init; }
    public string? Narrator { get; init; }
    public string? Rating { get; init; }
    public string? WikidataQid { get; init; }

    // Original input
    public string? FileName { get; init; }
    public string? FilePath { get; init; }
    public long? FileSizeBytes { get; init; }
    public string? ContentHash { get; init; }

    // Review data
    public Guid? ReviewItemId { get; init; }
    public string? ReviewTrigger { get; init; }
    public string? ReviewDetail { get; init; }
    public string? CandidatesJson { get; init; }
    public bool HasUserLocks { get; init; }

    // All canonical values for this entity
    public IReadOnlyList<RegistryCanonicalValue> CanonicalValues { get; init; } = [];

    // Claim history
    public IReadOnlyList<RegistryClaimRecord> ClaimHistory { get; init; } = [];
}

/// <summary>A canonical value with conflict and provider info.</summary>
public sealed record RegistryCanonicalValue(
    string Key,
    string Value,
    bool IsConflicted,
    string? WinningProviderId,
    bool NeedsReview,
    DateTimeOffset LastScoredAt);

/// <summary>A single claim from the voting history.</summary>
public sealed record RegistryClaimRecord(
    Guid Id,
    string ClaimKey,
    string ClaimValue,
    Guid ProviderId,
    double Confidence,
    bool IsUserLocked,
    DateTimeOffset ClaimedAt);
