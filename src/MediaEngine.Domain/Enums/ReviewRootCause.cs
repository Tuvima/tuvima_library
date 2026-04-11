namespace MediaEngine.Domain.Enums;

/// <summary>
/// Five internal root causes that compress 15+ review triggers.
/// The engine reasons about these states; the UI shows the specific
/// <see cref="ReviewTrigger"/> for user context.
/// </summary>
public enum ReviewRootCause
{
    /// <summary>
    /// Not enough data to identify the item.
    /// Maps from: RetailMatchFailed, StagedUnidentifiable, PlaceholderTitle, RootWatchFolder.
    /// </summary>
    InsufficientEvidence,

    /// <summary>
    /// Evidence exists but is contradictory or ambiguous.
    /// Maps from: RetailMatchAmbiguous, MultipleQidMatches, AmbiguousMediaType, LowConfidence.
    /// </summary>
    ConflictingEvidence,

    /// <summary>
    /// Retail matched but Wikidata couldn't confirm identity.
    /// Maps from: WikidataBridgeFailed, MissingQid, AuthorityMatchFailed, ContentMatchFailed.
    /// </summary>
    NoCanonicalIdentity,

    /// <summary>
    /// Identity resolved but enrichment has gaps.
    /// Maps from: ArtworkUnconfirmed, LanguageMismatch, WritebackFailed.
    /// </summary>
    EnrichmentIncomplete,

    /// <summary>
    /// Pipeline error, needs retry.
    /// Maps from: runtime exceptions.
    /// </summary>
    ProcessingFailure
}

/// <summary>
/// Extension methods for <see cref="ReviewRootCause"/>.
/// </summary>
public static class ReviewRootCauseExtensions
{
    /// <summary>Maps a <see cref="ReviewTrigger"/> string value to its root cause category.</summary>
    public static ReviewRootCause? FromTrigger(string? trigger) => trigger switch
    {
        "RetailMatchFailed" or "StagedUnidentifiable" or "PlaceholderTitle" or "RootWatchFolder"
            => ReviewRootCause.InsufficientEvidence,
        "RetailMatchAmbiguous" or "MultipleQidMatches" or "AmbiguousMediaType" or "LowConfidence"
            => ReviewRootCause.ConflictingEvidence,
        "WikidataBridgeFailed" or "MissingQid" or "AuthorityMatchFailed" or "ContentMatchFailed"
            => ReviewRootCause.NoCanonicalIdentity,
        "ArtworkUnconfirmed" or "LanguageMismatch" or "WritebackFailed"
            => ReviewRootCause.EnrichmentIncomplete,
        _ => null,
    };
}
