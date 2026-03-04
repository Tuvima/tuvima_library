namespace MediaEngine.Domain.Enums;

/// <summary>
/// String constants for the <c>trigger</c> column in <c>review_queue</c>.
///
/// Identifies why a review item was created. Stored as TEXT in SQLite for
/// readability and extensibility — future phases can introduce new triggers
/// without a schema migration.
/// </summary>
public static class ReviewTrigger
{
    /// <summary>
    /// The pipeline's overall confidence for the entity fell below the
    /// <c>auto_review_confidence_threshold</c> after all stages completed.
    /// </summary>
    public const string LowConfidence = "LowConfidence";

    /// <summary>
    /// The Wikidata bridge lookup returned multiple QID candidates and could
    /// not disambiguate automatically. The user must select the correct QID.
    /// </summary>
    public const string MultipleQidMatches = "MultipleQidMatches";

    /// <summary>
    /// The user explicitly requested a "Fix Match" action from the Dashboard,
    /// signalling that the current metadata assignment is incorrect.
    /// </summary>
    public const string UserFixMatch = "UserFixMatch";

    /// <summary>
    /// The <see cref="MediaEngine.Intelligence.HubArbiter"/> scored the entity in
    /// the NeedsReview disposition band (between Conflict and AutoLink thresholds).
    /// </summary>
    public const string ArbiterNeedsReview = "ArbiterNeedsReview";

    /// <summary>
    /// Stage 1 (Content Match) failed to find a match using the primary provider.
    /// The file's unique identifiers (ISBN, ASIN, etc.) and title did not resolve
    /// to any result. The user must manually select a match.
    /// </summary>
    public const string ContentMatchFailed = "ContentMatchFailed";

    /// <summary>
    /// Stage 2 (Universe Match) failed to link the entity to Wikidata.
    /// Bridge ID lookup, secondary ID lookup, and title search all either failed
    /// or returned confidence below the auto-accept threshold. The user must
    /// manually select a QID or skip universe matching.
    /// </summary>
    public const string UniverseMatchFailed = "UniverseMatchFailed";
}
