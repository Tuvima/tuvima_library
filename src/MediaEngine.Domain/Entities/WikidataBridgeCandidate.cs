namespace MediaEngine.Domain.Entities;

/// <summary>
/// A Wikidata entity considered during Stage 2 bridge resolution.
///
/// Every candidate is persisted so the review drawer can show which QIDs
/// were evaluated, how each matched, and why one was selected (or why
/// disambiguation is needed).
/// </summary>
public sealed class WikidataBridgeCandidate
{
    /// <summary>Unique identifier for this candidate.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Parent identity job.</summary>
    public Guid JobId { get; set; }

    /// <summary>Wikidata Q-identifier (e.g. "Q8337").</summary>
    public string Qid { get; set; } = "";

    /// <summary>Wikidata entity label.</summary>
    public string Label { get; set; } = "";

    /// <summary>Wikidata entity description.</summary>
    public string? Description { get; set; }

    /// <summary>How this candidate was found: "bridge_id", "text_reconciliation", or "ai_disambiguation".</summary>
    public string MatchedBy { get; set; } = "";

    /// <summary>Which bridge ID type matched (e.g. "isbn_13", "tmdb_id"). Null for text reconciliation.</summary>
    public string? BridgeIdType { get; set; }

    /// <summary>True if a strong bridge ID resolved directly to this entity.</summary>
    public bool IsExactMatch { get; set; }

    /// <summary>Composite score for constrained reconciliation candidates.</summary>
    public double ScoreTotal { get; set; }

    /// <summary>JSON score breakdown for constrained reconciliation (null for exact bridge matches).</summary>
    public string? ScoreBreakdownJson { get; set; }

    /// <summary>Outcome: "AutoAccepted", "NeedsReview", or "Rejected".</summary>
    public string Outcome { get; set; } = "Rejected";

    /// <summary>When this candidate was evaluated and persisted.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
