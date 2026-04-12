using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;

namespace MediaEngine.Intelligence.Tests;

/// <summary>
/// Test stub that delegates to the real <see cref="Services.FuzzyMatchingService"/>
/// so that IdentityMatcher and CollectionArbiter tests exercise actual fuzzy matching logic.
/// </summary>
internal sealed class StubFuzzyMatchingService : IFuzzyMatchingService
{
    private readonly Services.FuzzyMatchingService _real = new();

    public double ComputeTokenSetRatio(string a, string b) => _real.ComputeTokenSetRatio(a, b);
    public double ComputePartialRatio(string a, string b) => _real.ComputePartialRatio(a, b);
    public FieldMatchResult ScoreCandidate(LocalMetadata local, CandidateMetadata candidate)
        => _real.ScoreCandidate(local, candidate);
}
