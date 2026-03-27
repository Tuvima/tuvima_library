using MediaEngine.Domain.Models;
using MediaEngine.Intelligence.Services;

namespace MediaEngine.Intelligence.Tests;

/// <summary>
/// Unit tests for <see cref="FuzzyMatchingService"/>.
/// All tests exercise pure computation — no mocking required.
/// Covers TokenSetRatio, PartialRatio, and the composite ScoreCandidate method.
/// </summary>
public sealed class FuzzyMatchingTests
{
    private static FuzzyMatchingService Build() => new();

    // ── ComputeTokenSetRatio ──────────────────────────────────────────────────

    [Fact]
    public void TokenSetRatio_IdenticalStrings_Returns1()
    {
        var svc = Build();
        var result = svc.ComputeTokenSetRatio("Frank Herbert", "Frank Herbert");
        Assert.Equal(1.0, result);
    }

    [Fact]
    public void TokenSetRatio_ReorderedWords_ReturnsHigh()
    {
        var svc = Build();
        // Token-set ratio should handle word reordering well
        var result = svc.ComputeTokenSetRatio("Frank Herbert", "Herbert Frank");
        Assert.True(result >= 0.9, $"Expected >= 0.9 but got {result}");
    }

    [Fact]
    public void TokenSetRatio_CompletelyDifferent_ReturnsLow()
    {
        var svc = Build();
        var result = svc.ComputeTokenSetRatio("Dune", "Neuromancer");
        Assert.True(result < 0.5, $"Expected < 0.5 but got {result}");
    }

    [Fact]
    public void TokenSetRatio_EmptyString_Returns0()
    {
        var svc = Build();
        var result = svc.ComputeTokenSetRatio(string.Empty, "anything");
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void TokenSetRatio_CaseInsensitive()
    {
        var svc = Build();
        var result = svc.ComputeTokenSetRatio("dune", "DUNE");
        Assert.Equal(1.0, result);
    }

    [Fact]
    public void TokenSetRatio_BothEmpty_Returns0()
    {
        var svc = Build();
        var result = svc.ComputeTokenSetRatio(string.Empty, string.Empty);
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void TokenSetRatio_CommaDelimited_ReorderedName_ReturnsHigh()
    {
        var svc = Build();
        // "Herbert, Frank" should score high against "Frank Herbert"
        // comma is a tokenizer separator so tokens are identical sets
        var result = svc.ComputeTokenSetRatio("Frank Herbert", "Herbert, Frank");
        Assert.True(result >= 0.9, $"Expected >= 0.9 but got {result}");
    }

    // ── ComputePartialRatio ───────────────────────────────────────────────────

    [Fact]
    public void PartialRatio_SubstringMatch_ReturnsHigh()
    {
        var svc = Build();
        var result = svc.ComputePartialRatio("Dune", "Dune: Part One");
        Assert.True(result >= 0.8, $"Expected >= 0.8 but got {result}");
    }

    [Fact]
    public void PartialRatio_ExactMatch_Returns1()
    {
        var svc = Build();
        var result = svc.ComputePartialRatio("Dune", "Dune");
        Assert.Equal(1.0, result);
    }

    [Fact]
    public void PartialRatio_NoOverlap_ReturnsLow()
    {
        var svc = Build();
        var result = svc.ComputePartialRatio("abc", "xyz");
        Assert.True(result < 0.5, $"Expected < 0.5 but got {result}");
    }

    [Fact]
    public void PartialRatio_EmptyString_Returns0()
    {
        var svc = Build();
        var result = svc.ComputePartialRatio(string.Empty, "anything");
        Assert.Equal(0.0, result);
    }

    // ── ScoreCandidate composite ──────────────────────────────────────────────

    [Fact]
    public void ScoreCandidate_TitleOnly_UsesFullTitleWeight()
    {
        var svc = Build();
        // When author/year/format are null, only title contributes
        var local = new LocalMetadata("Dune", null, null, null);
        var candidate = new CandidateMetadata("Dune", null, null, null);

        var result = svc.ScoreCandidate(local, candidate);

        // Perfect title match with only title contributing → 1.0
        Assert.Equal(1.0, result.CompositeScore, precision: 10);
        Assert.Equal(FieldMatchVerdict.Exact, result.TitleVerdict);
    }

    [Fact]
    public void ScoreCandidate_SequelSafe_NumberMismatchPenalized()
    {
        var svc = Build();
        // "Harry Potter 1" vs "Harry Potter 2" — numbers differ, should penalise heavily
        var local = new LocalMetadata("Harry Potter 1", null, null, null);
        var candidate = new CandidateMetadata("Harry Potter 2", null, null, null);

        var result = svc.ScoreCandidate(local, candidate);

        Assert.True(result.CompositeScore < 0.8,
            $"Expected composite < 0.8 for number mismatch but got {result.CompositeScore}");
    }

    [Fact]
    public void ScoreCandidate_SequelSafe_NumberMatch_HighScore()
    {
        var svc = Build();
        var local = new LocalMetadata("Harry Potter 1", null, null, null);
        var candidate = new CandidateMetadata("Harry Potter 1", null, null, null);

        var result = svc.ScoreCandidate(local, candidate);

        Assert.True(result.CompositeScore >= 0.95,
            $"Expected composite >= 0.95 for matching sequel number but got {result.CompositeScore}");
    }

    [Fact]
    public void ScoreCandidate_HighCoverSimilarity_ScoresBetterThanZeroCover()
    {
        // When two candidates have the same imperfect title score, the one with
        // a high cover similarity should beat the one with zero cover similarity.
        var svc = Build();
        // Use a slightly different title to give a non-perfect base score
        var local = new LocalMetadata("Dune", null, null, null);

        var candidateHighCover = new CandidateMetadata("Dune", null, null, null)
        {
            CoverSimilarity = 0.95
        };
        var candidateZeroCover = new CandidateMetadata("Dune", null, null, null)
        {
            CoverSimilarity = 0.0   // Cover present but completely different
        };

        var resultHighCover = svc.ScoreCandidate(local, candidateHighCover);
        var resultZeroCover = svc.ScoreCandidate(local, candidateZeroCover);

        // High cover (0.95) should produce a higher composite than zero cover (0.0)
        Assert.True(resultHighCover.CompositeScore > resultZeroCover.CompositeScore,
            $"High cover similarity should score better than zero cover: high={resultHighCover.CompositeScore}, zero={resultZeroCover.CompositeScore}");
    }

    [Fact]
    public void ScoreCandidate_NoCoverAvailable_MinusCoverSimilarity_ExcludedFromComposite()
    {
        // CoverSimilarity=-1 means "not available" and should be excluded from composite weights.
        // This means only title weight (0.45) contributes → composite equals title score.
        var svc = Build();
        var local = new LocalMetadata("Dune", null, null, null);
        var candidate = new CandidateMetadata("Dune", null, null, null)
        {
            // Default CoverSimilarity = -1.0 (not available)
        };

        var result = svc.ScoreCandidate(local, candidate);

        // Perfect title, nothing else → composite = 1.0 (only title weight normalizes)
        Assert.Equal(1.0, result.CompositeScore, precision: 10);
        Assert.Equal(FieldMatchVerdict.NotAvailable, result.CoverVerdict);
    }

    [Fact]
    public void ScoreCandidate_AllFields_MismatchedEverything_LowComposite()
    {
        var svc = Build();
        var local = new LocalMetadata("Dune", "Frank Herbert", "1965", "EPUB");
        var candidate = new CandidateMetadata("Neuromancer", "William Gibson", "1984", "MOVIE");

        var result = svc.ScoreCandidate(local, candidate);

        Assert.True(result.CompositeScore < 0.5,
            $"Expected composite < 0.5 for completely mismatched data but got {result.CompositeScore}");
    }

    [Fact]
    public void ScoreCandidate_AllFieldsMatch_HighComposite()
    {
        var svc = Build();
        var local = new LocalMetadata("Dune", "Frank Herbert", "1965", "EPUB");
        var candidate = new CandidateMetadata("Dune", "Frank Herbert", "1965", "EPUB");

        var result = svc.ScoreCandidate(local, candidate);

        Assert.True(result.CompositeScore >= 0.95,
            $"Expected composite >= 0.95 for fully matching data but got {result.CompositeScore}");
    }

    [Fact]
    public void ScoreCandidate_YearExactMatch_Scores1()
    {
        var svc = Build();
        var local = new LocalMetadata("Dune", null, "1965", null);
        var candidate = new CandidateMetadata("Dune", null, "1965", null);

        var result = svc.ScoreCandidate(local, candidate);

        Assert.Equal(1.0, result.YearScore);
        Assert.Equal(FieldMatchVerdict.Exact, result.YearVerdict);
    }

    [Fact]
    public void ScoreCandidate_YearOffByOne_ScoresHalf()
    {
        var svc = Build();
        var local = new LocalMetadata("Dune", null, "1965", null);
        var candidate = new CandidateMetadata("Dune", null, "1966", null);

        var result = svc.ScoreCandidate(local, candidate);

        Assert.Equal(0.5, result.YearScore);
    }

    [Fact]
    public void ScoreCandidate_RelatedBookFormats_PartialFormatCredit()
    {
        var svc = Build();
        // EPUB vs AUDIOBOOK — book family → 0.5 format score
        var local = new LocalMetadata("Dune", null, null, "EPUB");
        var candidate = new CandidateMetadata("Dune", null, null, "AUDIOBOOK");

        var result = svc.ScoreCandidate(local, candidate);

        Assert.Equal(0.5, result.FormatScore, precision: 5);
    }

    [Fact]
    public void ScoreCandidate_NullTitleInLocal_Returns0TitleScore()
    {
        var svc = Build();

        // LocalMetadata.Title is required (non-null record param) so pass empty string
        var local = new LocalMetadata(string.Empty, null, null, null);
        var candidate = new CandidateMetadata("Dune", null, null, null);

        var result = svc.ScoreCandidate(local, candidate);

        Assert.Equal(0.0, result.TitleScore);
    }
}
