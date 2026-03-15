namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Centralized gate that determines whether a media asset can be promoted
/// from staging into the organized library. All promotion paths must call
/// this gate — there should be no other code paths that make organization
/// decisions independently.
/// </summary>
public interface IOrganizationGate
{
    /// <summary>
    /// Evaluates whether the given asset metadata passes all organization
    /// requirements. Returns a result with the decision and reason.
    /// </summary>
    GateResult Evaluate(
        double overallConfidence,
        IReadOnlyDictionary<string, string> canonicalValues,
        bool hasUserLock,
        bool mediaTypeNeedsReview,
        string? resolvedRelativePath);
}

/// <param name="CanOrganize">True if the file may be promoted to the library.</param>
/// <param name="BlockReason">Human-readable explanation when blocked; null when allowed.</param>
/// <param name="StagingSubcategory">When blocked: which staging tier to use (pending, low-confidence, unidentifiable, other).</param>
/// <param name="ReviewTrigger">When blocked: optional ReviewTrigger constant for review queue creation.</param>
/// <param name="ReviewDetail">When blocked: detail string for the review queue entry.</param>
public record GateResult(
    bool CanOrganize,
    string? BlockReason,
    string StagingSubcategory = "pending",
    string? ReviewTrigger = null,
    string? ReviewDetail = null);
