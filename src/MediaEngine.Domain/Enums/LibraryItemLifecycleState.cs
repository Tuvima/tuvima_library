namespace MediaEngine.Domain.Enums;

/// <summary>
/// The four lifecycle states for items in the LibraryItem.
///
/// These states are determined by a combination of:
/// - The <c>curator_state</c> column on the <c>works</c> table (Provisional, Rejected)
/// - The presence of pending <c>review_queue</c> entries (InReview)
/// - Default (Registered — has valid QID, no pending review)
///
/// Replaces the former Registered/NeedsReview/NoMatch/Failed model.
/// </summary>
public static class LibraryItemLifecycleState
{
    /// <summary>
    /// Matched to a Wikidata QID, visible in the Library.
    /// The item has been successfully identified and enriched.
    /// </summary>
    public const string Registered = "Registered";

    /// <summary>
    /// The hydration pipeline or a user report has flagged this item for curator attention.
    /// At least one pending <c>review_queue</c> entry exists.
    /// </summary>
    public const string InReview = "InReview";

    /// <summary>
    /// Curator-created metadata for items that Wikidata cannot identify.
    /// Visible in the Library. The system periodically re-checks Wikidata
    /// and offers to upgrade to Registered if a match appears.
    /// </summary>
    public const string Provisional = "Provisional";

    /// <summary>
    /// Curator explicitly rejected this item. The file is moved to
    /// <c>.staging/rejected/</c> and awaits auto-purge after the configured
    /// retention period (default: 14 days).
    /// </summary>
    public const string Rejected = "Rejected";
}
