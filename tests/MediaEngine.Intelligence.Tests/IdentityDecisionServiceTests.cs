using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Intelligence.Services;
using MediaEngine.Intelligence.Strategies;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaEngine.Intelligence.Tests;

/// <summary>
/// Tests for <see cref="IdentityDecisionService"/> — verifies that retail, Wikidata,
/// and organization-readiness decisions are set correctly on the context for every
/// confidence band and resolution method combination.
/// </summary>
public sealed class IdentityDecisionServiceTests
{
    // ── Service construction ─────────────────────────────────────────────────

    private static IdentityDecisionService CreateService() =>
        new(
            new List<IMediaTypeIdentityStrategy>
            {
                new BookIdentityStrategy(),
                new AudiobookIdentityStrategy(),
                new MovieIdentityStrategy(),
                new TvIdentityStrategy(),
                new MusicIdentityStrategy(),
                new ComicIdentityStrategy(),
            },
            NullLogger<IdentityDecisionService>.Instance);

    // ── Context builder ──────────────────────────────────────────────────────

    /// <summary>
    /// Builds an <see cref="IdentityResolutionContext"/> with sensible defaults.
    /// Callers override only the properties relevant to the test being written.
    /// </summary>
    private static IdentityResolutionContext MakeContext(
        MediaType mediaType = MediaType.Books,
        double retailScore = 0.0,
        bool hasUserLocks = false,
        string? resolvedQid = null,
        string? resolutionMethod = null,
        IReadOnlyList<MetadataClaim>? fileMetadataClaims = null,
        IReadOnlyDictionary<string, string>? canonicalValues = null)
    {
        return new IdentityResolutionContext
        {
            EntityId            = Guid.NewGuid(),
            MediaType           = mediaType,
            RetailScore         = retailScore,
            HasUserLocks        = hasUserLocks,
            ResolvedQid         = resolvedQid,
            ResolutionMethod    = resolutionMethod,
            FileMetadataClaims  = fileMetadataClaims ?? [],
            CanonicalValues     = canonicalValues
                                  ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        };
    }

    /// <summary>Creates a <see cref="MetadataClaim"/> for file-processor claims.</summary>
    private static MetadataClaim MakeClaim(string key, string value, double confidence = 1.0) =>
        new()
        {
            Id         = Guid.NewGuid(),
            EntityId   = Guid.NewGuid(),
            ProviderId = Guid.NewGuid(),
            ClaimKey   = key,
            ClaimValue = value,
            Confidence = confidence,
        };

    // ════════════════════════════════════════════════════════════════════════
    //  EvaluateRetailOutcome
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void EvaluateRetailOutcome_Accept_WhenScoreExact()
    {
        // Score 0.96 falls in the "Exact" band — decision must be Accept.
        var service = CreateService();
        var ctx     = MakeContext(retailScore: 0.96);
        var strategy = new BookIdentityStrategy();

        service.EvaluateRetailOutcome(ctx, strategy);

        Assert.Equal(IdentityDecision.Accept, ctx.Decision);
        Assert.Null(ctx.ReviewCause);
    }

    [Fact]
    public void EvaluateRetailOutcome_Accept_WhenScoreStrong()
    {
        // Score 0.88 falls in the "Strong" band — also auto-accepted.
        var service  = CreateService();
        var ctx      = MakeContext(retailScore: 0.88);
        var strategy = new BookIdentityStrategy();

        service.EvaluateRetailOutcome(ctx, strategy);

        Assert.Equal(IdentityDecision.Accept, ctx.Decision);
        Assert.Null(ctx.ReviewCause);
    }

    [Fact]
    public void EvaluateRetailOutcome_ProvisionalAccept_WhenScoreProvisional()
    {
        // Score 0.60 falls in the "Provisional" band — accepted with review flag.
        var service  = CreateService();
        var ctx      = MakeContext(retailScore: 0.60);
        var strategy = new BookIdentityStrategy();

        service.EvaluateRetailOutcome(ctx, strategy);

        Assert.Equal(IdentityDecision.ProvisionalAccept, ctx.Decision);
        Assert.Null(ctx.ReviewCause);
    }

    [Fact]
    public void EvaluateRetailOutcome_Review_WhenScoreInsufficient_NoFallback()
    {
        // Music strategy has AllowsTextFallback = false.
        // Score 0.20 is "Insufficient" and there is no fallback path, so → Review.
        var service  = CreateService();
        var ctx      = MakeContext(mediaType: MediaType.Music, retailScore: 0.20);
        var strategy = new MusicIdentityStrategy();

        service.EvaluateRetailOutcome(ctx, strategy);

        Assert.Equal(IdentityDecision.Review, ctx.Decision);
        Assert.Equal(ReviewRootCause.InsufficientEvidence, ctx.ReviewCause);
    }

    [Fact]
    public void EvaluateRetailOutcome_Accept_WhenUserLocked()
    {
        // Tier A: HasUserLocks = true overrides any retail score (including 0.0).
        var service  = CreateService();
        var ctx      = MakeContext(retailScore: 0.0, hasUserLocks: true);
        var strategy = new BookIdentityStrategy();

        service.EvaluateRetailOutcome(ctx, strategy);

        Assert.Equal(IdentityDecision.Accept, ctx.Decision);
        Assert.Null(ctx.ReviewCause);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  EvaluateWikidataOutcome
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void EvaluateWikidataOutcome_Accept_WhenBridgeResolved()
    {
        // A QID resolved via a bridge ID (e.g. ISBN → Q-identifier) is high
        // confidence and must produce Accept.
        var service = CreateService();
        var ctx     = MakeContext(resolvedQid: "Q190192", resolutionMethod: "bridge");

        service.EvaluateWikidataOutcome(ctx);

        Assert.Equal(IdentityDecision.Accept, ctx.Decision);
        Assert.Null(ctx.ReviewCause);
    }

    [Fact]
    public void EvaluateWikidataOutcome_ProvisionalAccept_WhenTextResolved()
    {
        // A QID resolved by CirrusSearch text match is lower confidence.
        var service = CreateService();
        var ctx     = MakeContext(resolvedQid: "Q190192", resolutionMethod: "text");

        service.EvaluateWikidataOutcome(ctx);

        Assert.Equal(IdentityDecision.ProvisionalAccept, ctx.Decision);
        Assert.Null(ctx.ReviewCause);
    }

    [Fact]
    public void EvaluateWikidataOutcome_Review_WhenNoQid()
    {
        // No QID resolved — the canonical identity is unknown, so route to review.
        var service = CreateService();
        var ctx     = MakeContext(resolvedQid: null, resolutionMethod: null);

        service.EvaluateWikidataOutcome(ctx);

        Assert.Equal(IdentityDecision.Review, ctx.Decision);
        Assert.Equal(ReviewRootCause.NoCanonicalIdentity, ctx.ReviewCause);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  EvaluateOrganizationReadiness
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void EvaluateOrganizationReadiness_True_WhenTitleExists()
    {
        // A valid, non-placeholder title is the minimum requirement for file organization.
        var service = CreateService();
        var ctx     = MakeContext(
            canonicalValues: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [MetadataFieldConstants.Title] = "Dune",
            });

        Assert.True(service.EvaluateOrganizationReadiness(ctx));
    }

    [Fact]
    public void EvaluateOrganizationReadiness_False_WhenTitleMissing()
    {
        // No "title" key in CanonicalValues — cannot organize without a name.
        var service = CreateService();
        var ctx     = MakeContext(
            canonicalValues: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [MetadataFieldConstants.Author] = "Frank Herbert",
            });

        Assert.False(service.EvaluateOrganizationReadiness(ctx));
    }

    [Fact]
    public void EvaluateOrganizationReadiness_False_WhenTitleIsPlaceholder()
    {
        // "Untitled" is a known placeholder — must be treated as missing.
        var service = CreateService();
        var ctx     = MakeContext(
            canonicalValues: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [MetadataFieldConstants.Title] = "Untitled",
            });

        Assert.False(service.EvaluateOrganizationReadiness(ctx));
    }
}
