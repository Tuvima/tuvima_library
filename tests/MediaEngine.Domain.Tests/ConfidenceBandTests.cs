using MediaEngine.Domain.Constants;

namespace MediaEngine.Domain.Tests;

/// <summary>
/// Tests for <see cref="ConfidenceBand"/> — verifies that <see cref="ConfidenceBand.Classify"/>
/// returns the correct band name at and above every defined threshold boundary.
/// </summary>
public sealed class ConfidenceBandTests
{
    // ── Exact (≥ 0.95) ───────────────────────────────────────────────────────

    [Fact]
    public void Classify_ReturnsExact_AtThreshold()
    {
        // ExactFloor is 0.95 — the boundary score itself must resolve to "Exact".
        Assert.Equal("Exact", ConfidenceBand.Classify(0.95));
    }

    [Fact]
    public void Classify_ReturnsExact_AboveThreshold()
    {
        // A perfect score of 1.0 must still resolve to "Exact" (top of the range).
        Assert.Equal("Exact", ConfidenceBand.Classify(1.0));
    }

    // ── Strong (≥ 0.85, < 0.95) ──────────────────────────────────────────────

    [Fact]
    public void Classify_ReturnsStrong_AtThreshold()
    {
        // StrongFloor is 0.85 — just at the boundary, below Exact.
        Assert.Equal("Strong", ConfidenceBand.Classify(0.85));
    }

    // ── Provisional (≥ 0.50, < 0.85) ─────────────────────────────────────────

    [Fact]
    public void Classify_ReturnsProvisional_AtThreshold()
    {
        // ProvisionalFloor is 0.50 — right at the lower edge of the accept-with-flag zone.
        Assert.Equal("Provisional", ConfidenceBand.Classify(0.50));
    }

    // ── Ambiguous (≥ 0.30, < 0.50) ───────────────────────────────────────────

    [Fact]
    public void Classify_ReturnsAmbiguous_AtThreshold()
    {
        // AmbiguousFloor is 0.30 — at the boundary, needs manual review.
        Assert.Equal("Ambiguous", ConfidenceBand.Classify(0.30));
    }

    // ── Insufficient (< 0.30) ────────────────────────────────────────────────

    [Fact]
    public void Classify_ReturnsInsufficient_BelowAmbiguous()
    {
        // One point below AmbiguousFloor (0.30) falls into the reject band.
        Assert.Equal("Insufficient", ConfidenceBand.Classify(0.29));
    }

    [Fact]
    public void Classify_ReturnsInsufficient_AtZero()
    {
        // Zero is the lowest possible score and must be "Insufficient".
        Assert.Equal("Insufficient", ConfidenceBand.Classify(0.0));
    }
}
