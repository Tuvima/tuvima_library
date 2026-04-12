namespace MediaEngine.Intelligence.Models;

/// <summary>
/// The outcome of a Collection-Arbiter evaluation for a single Work.
/// Spec: Phase 6 – Threshold Enforcement; Low Confidence Flags.
/// </summary>
public enum LinkDisposition
{
    /// <summary>
    /// Score ≥ <c>AutoLinkThreshold</c>.
    /// The Work is automatically assigned to the matched Collection; no human
    /// intervention required.
    /// </summary>
    AutoLinked,

    /// <summary>
    /// Score ∈ [<c>ConflictThreshold</c>, <c>AutoLinkThreshold</c>).
    /// Confidence is above the noise floor but below the auto-link bar;
    /// a human reviewer must confirm or reject the proposed link.
    /// Spec: Phase 6 – "Entities with scores between the conflict_threshold and
    /// auto_link_threshold MUST be assigned a NeedsReview status."
    /// </summary>
    NeedsReview,

    /// <summary>
    /// Score &lt; <c>ConflictThreshold</c>.
    /// The match is too weak to be useful; the Work remains unlinked (or
    /// stays in the System-Default Collection).
    /// </summary>
    Rejected,
}
