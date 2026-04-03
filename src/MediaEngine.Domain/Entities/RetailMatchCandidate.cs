namespace MediaEngine.Domain.Entities;

/// <summary>
/// A single candidate returned by a retail provider during Stage 1 identification.
///
/// Every candidate is persisted — not just the winner — so the Action Center can
/// show the user all options and why each scored the way it did.
/// Score breakdown is stored as JSON for full transparency.
/// </summary>
public sealed class RetailMatchCandidate
{
    /// <summary>Unique identifier for this candidate.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Parent identity job.</summary>
    public Guid JobId { get; set; }

    /// <summary>Which provider returned this candidate.</summary>
    public Guid ProviderId { get; set; }

    /// <summary>Provider display name (e.g. "Apple Books", "TMDB").</summary>
    public string ProviderName { get; set; } = "";

    /// <summary>Provider item identifier in the external catalogue.</summary>
    public string? ProviderItemId { get; set; }

    /// <summary>Provider rank in the pipeline (from <c>pipelines.json</c>).</summary>
    public int Rank { get; set; }

    /// <summary>Candidate title as returned by the provider.</summary>
    public string Title { get; set; } = "";

    /// <summary>Candidate author/artist/director.</summary>
    public string? Creator { get; set; }

    /// <summary>Candidate year or release date.</summary>
    public string? Year { get; set; }

    /// <summary>Composite score from <c>RetailMatchScoringService</c>.</summary>
    public double ScoreTotal { get; set; }

    /// <summary>JSON breakdown: <c>{"title": 0.42, "author": 0.33, "year": 0.10, "format": 0.08}</c>.</summary>
    public string? ScoreBreakdownJson { get; set; }

    /// <summary>JSON dictionary of bridge IDs this candidate carried (ISBN, ASIN, TMDB ID, etc.).</summary>
    public string? BridgeIdsJson { get; set; }

    /// <summary>Retail description/synopsis.</summary>
    public string? Description { get; set; }

    /// <summary>Cover art URL from the provider.</summary>
    public string? ImageUrl { get; set; }

    /// <summary>Outcome: "AutoAccepted", "Ambiguous", or "Rejected".</summary>
    public string Outcome { get; set; } = "Rejected";

    /// <summary>When this candidate was scored and persisted.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
