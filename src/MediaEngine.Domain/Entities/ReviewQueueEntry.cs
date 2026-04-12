namespace MediaEngine.Domain.Entities;

/// <summary>
/// A single entry in the review queue.
///
/// Created when the hydration pipeline cannot proceed automatically — either
/// because Wikidata returned multiple QID candidates, the overall confidence
/// fell below the review threshold, or the Collection Arbiter scored the entity in
/// the NeedsReview disposition band.
///
/// The user resolves the entry by selecting the correct QID, confirming field
/// overrides, or dismissing the item as irrelevant.
///
/// Stored in the <c>review_queue</c> table (migration M-013).
/// See <see cref="Enums.ReviewTrigger"/> and <see cref="Enums.ReviewStatus"/>.
/// </summary>
public sealed class ReviewQueueEntry
{
    /// <summary>Unique identifier for this review item.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The domain entity this review targets.
    /// Points to <c>media_assets.id</c>, <c>works.id</c>, etc.
    /// </summary>
    public Guid EntityId { get; set; }

    /// <summary>
    /// The kind of entity being reviewed.
    /// Stored as TEXT for readability (e.g. "Work", "Person").
    /// </summary>
    public required string EntityType { get; init; }

    /// <summary>
    /// Why this review item was created.
    /// Use <see cref="Enums.ReviewTrigger"/> constants.
    /// </summary>
    public required string Trigger { get; init; }

    /// <summary>
    /// Current lifecycle status.
    /// Use <see cref="Enums.ReviewStatus"/> constants.
    /// Defaults to <see cref="Enums.ReviewStatus.Pending"/>.
    /// </summary>
    public string Status { get; set; } = Enums.ReviewStatus.Pending;

    /// <summary>
    /// The Wikidata QID that the pipeline proposed before halting (nullable).
    /// Present when the trigger is <see cref="Enums.ReviewTrigger.MultipleQidMatches"/>
    /// and the pipeline had a best-guess candidate.
    /// </summary>
    public string? ProposedCollectionId { get; set; }

    /// <summary>
    /// The pipeline's confidence score at the time the review was created (nullable).
    /// Used for sorting and threshold-based bulk operations.
    /// </summary>
    public double? ConfidenceScore { get; set; }

    /// <summary>
    /// JSON array of QID disambiguation candidates (nullable).
    /// Each element has <c>qid</c>, <c>label</c>, and optional <c>description</c>.
    /// Present when trigger is <see cref="Enums.ReviewTrigger.MultipleQidMatches"/>.
    /// </summary>
    public string? CandidatesJson { get; set; }

    /// <summary>
    /// Human-readable detail text explaining why the review was created (nullable).
    /// </summary>
    public string? Detail { get; set; }

    /// <summary>When this review item was created (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When this review item was resolved or dismissed (nullable).</summary>
    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>
    /// The profile ID of the user who resolved or dismissed this item (nullable).
    /// Null for automated resolutions.
    /// </summary>
    public string? ResolvedBy { get; set; }

    /// <summary>Engine-level root cause (5 categories). UI shows the specific Trigger for detail.</summary>
    public string? RootCause { get; set; }
}
