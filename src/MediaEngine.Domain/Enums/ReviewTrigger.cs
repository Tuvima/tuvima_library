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
    /// Stage 1 (Authority Match) failed to identify the work via Wikidata.
    /// Bridge ID lookup (ISBN, ASIN, etc.) and title search both returned no match.
    /// The pipeline continues to Stage 3 (Retail Match) but the user should review.
    /// </summary>
    public const string AuthorityMatchFailed = "AuthorityMatchFailed";

    /// <summary>
    /// Stage 3 (Retail Match) failed to find a match using any retail provider.
    /// The file's identifiers and title did not resolve to any result.
    /// The user must manually select a match or provide metadata.
    /// </summary>
    public const string ContentMatchFailed = "ContentMatchFailed";

    /// <summary>
    /// Legacy: Stage 2 (Universe Match) failed. Superseded by
    /// <see cref="AuthorityMatchFailed"/> when the pipeline was reversed.
    /// Kept for backward compatibility with existing review queue rows.
    /// </summary>
    public const string UniverseMatchFailed = "UniverseMatchFailed";

    /// <summary>
    /// The scoring engine detected a metadata conflict: two or more claims for
    /// the same field have confidence values within the conflict epsilon. The file
    /// is still organised with the best guess, but the user should verify the
    /// conflicting field.
    /// </summary>
    public const string MetadataConflict = "MetadataConflict";

    /// <summary>
    /// The media type could not be determined with sufficient confidence.
    /// The file's container format is shared across multiple media types
    /// (e.g. MP3 → Audiobook/Music/Podcast, MP4 → Movie/TV Show) and
    /// heuristic analysis did not produce a clear winner. The user must
    /// manually select the correct media type.
    /// </summary>
    public const string AmbiguousMediaType = "AmbiguousMediaType";

    /// <summary>
    /// Wikidata (Stage 1) did not find a QID for the work, but a retail
    /// provider (Stage 3) matched by ISBN/ASIN.  The book is likely new or
    /// in early release.  A <c>NF{6-digit}</c> placeholder QID was assigned
    /// and should be replaced with a real QID when Wikidata catches up.
    /// </summary>
    public const string MissingQid = "MissingQid";

    /// <summary>
    /// One or more canonical value fields were flagged for review (conflicted,
    /// missing expected field, or unconfirmed local-only source).
    /// </summary>
    public const string FieldLevelReview = "FieldLevelReview";

    /// <summary>
    /// File scored below 0.40 overall confidence with no user-locked claims.
    /// Moved to .staging/unidentifiable/ — deeply broken or unrecognizable.
    /// </summary>
    public const string StagedUnidentifiable = "StagedUnidentifiable";

    /// <summary>
    /// Legacy alias for <see cref="StagedUnidentifiable"/>.
    /// Kept for backward compatibility with existing review queue rows.
    /// </summary>
    [Obsolete("Use StagedUnidentifiable instead.")]
    public const string OrphanedUnidentifiable = "OrphanedUnidentifiable";

    /// <summary>
    /// The file's title is a placeholder ("Unknown", "Untitled", blank) and
    /// no bridge identifier (ISBN, ASIN, QID) exists to prove its identity.
    /// The user must provide a real title or match it manually.
    /// </summary>
    public const string PlaceholderTitle = "PlaceholderTitle";
}
