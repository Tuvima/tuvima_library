using MediaEngine.Domain.Entities;

namespace MediaEngine.Intelligence.Models;

/// <summary>
/// The output of a single <see cref="Contracts.IConflictResolver.Resolve"/> call.
///
/// Contains the winning <see cref="MetadataClaim"/> and its adjusted confidence
/// after provider-weight and stale-decay factors have been applied.
///
/// Spec: Phase 6 – Claim Arbitration; IConflictResolver.
/// </summary>
public sealed class ClaimResolution
{
    /// <summary>The claim whose value won the arbitration for this field.</summary>
    public required MetadataClaim WinningClaim { get; init; }

    /// <summary>
    /// The winning value-group's total normalised weight after applying
    /// provider weights and stale-decay.  Range: (0.0, 1.0].
    /// </summary>
    public required double AdjustedConfidence { get; init; }

    /// <summary>
    /// Human-readable summary of how the winner was selected
    /// (e.g. "Single claim, confidence 1.0" or "Won with weight 0.87; 2 rivals").
    /// Useful for audit logging and debugging.
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// <see langword="true"/> when the runner-up's weight is within
    /// <see cref="ScoringConfiguration.ConflictEpsilon"/> of the winner's.
    /// The parent <see cref="FieldScore.IsConflicted"/> is set from this flag.
    /// </summary>
    public bool IsConflicted { get; init; }
}
