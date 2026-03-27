using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Services;

/// <summary>
/// Abstracts fuzzy string comparison for candidate matching.
/// Implementation uses native Levenshtein distance (no external dependencies) in MediaEngine.Intelligence.
/// </summary>
public interface IFuzzyMatchingService
{
    /// <summary>Token-order-insensitive comparison. Handles "Herbert, Frank" vs "Frank Herbert".</summary>
    double ComputeTokenSetRatio(string a, string b);

    /// <summary>Partial substring match. Handles "Dune: Part One" vs "Dune".</summary>
    double ComputePartialRatio(string a, string b);

    /// <summary>Composite field-by-field scoring with sequel-safe numeric extraction.</summary>
    FieldMatchResult ScoreCandidate(LocalMetadata local, CandidateMetadata candidate);
}
