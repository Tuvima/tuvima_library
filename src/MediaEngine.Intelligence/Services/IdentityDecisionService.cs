using MediaEngine.Domain;
using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Intelligence.Services;

/// <summary>
/// Single authority for all identity accept/review/retry decisions in the pipeline.
/// Workers call this service and act on the returned verdict — they never check
/// thresholds or band boundaries themselves.
///
/// The service is stateless. All mutable state lives in the
/// <see cref="IdentityResolutionContext"/> passed in by the caller.
/// </summary>
public sealed class IdentityDecisionService
{
    private readonly IReadOnlyDictionary<MediaType, IMediaTypeIdentityStrategy> _strategies;
    private readonly ILogger<IdentityDecisionService> _logger;

    // Creator claim keys used when checking text-fallback eligibility.
    private static readonly IReadOnlyList<string> CreatorClaimKeys =
    [
        MetadataFieldConstants.Author,
        MetadataFieldConstants.Artist,
        MetadataFieldConstants.Director,
    ];

    // Placeholder titles that should route to review rather than accepting.
    private static readonly IReadOnlySet<string> PlaceholderTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Unknown",
        "Untitled",
    };

    public IdentityDecisionService(
        IEnumerable<IMediaTypeIdentityStrategy> strategies,
        ILogger<IdentityDecisionService> logger)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        ArgumentNullException.ThrowIfNull(logger);

        _strategies = strategies.ToDictionary(s => s.MediaType, s => s);
        _logger = logger;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Evaluates the retail (Stage 1) outcome and sets
    /// <see cref="IdentityResolutionContext.Decision"/> and
    /// <see cref="IdentityResolutionContext.ReviewCause"/> accordingly.
    ///
    /// Decision rules:
    /// <list type="bullet">
    ///   <item><see cref="IdentityResolutionContext.HasUserLocks"/> → <see cref="IdentityDecision.Accept"/> (Tier A always wins)</item>
    ///   <item>Retail band Exact or Strong → <see cref="IdentityDecision.Accept"/></item>
    ///   <item>Retail band Provisional → <see cref="IdentityDecision.ProvisionalAccept"/></item>
    ///   <item>Retail band Ambiguous or Insufficient, text fallback eligible → <see cref="IdentityDecision.ProvisionalAccept"/></item>
    ///   <item>Retail band Ambiguous or Insufficient, no fallback → <see cref="IdentityDecision.Review"/> (<see cref="ReviewRootCause.InsufficientEvidence"/>)</item>
    /// </list>
    /// </summary>
    public void EvaluateRetailOutcome(IdentityResolutionContext context, IMediaTypeIdentityStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(strategy);

        // Tier A: user locks always win regardless of retail confidence.
        if (context.HasUserLocks)
        {
            SetDecision(context, IdentityDecision.Accept, rootCause: null,
                band: context.RetailBand, method: "user-lock");
            return;
        }

        var band = context.RetailBand;

        switch (band)
        {
            case "Exact":
            case "Strong":
                SetDecision(context, IdentityDecision.Accept, rootCause: null,
                    band: band, method: "retail");
                return;

            case "Provisional":
                SetDecision(context, IdentityDecision.ProvisionalAccept, rootCause: null,
                    band: band, method: "retail");
                return;

            default:
                // "Ambiguous" or "Insufficient" — check whether text fallback can save us.
                if (EvaluateTextFallbackEligibility(context, strategy))
                {
                    SetDecision(context, IdentityDecision.ProvisionalAccept, rootCause: null,
                        band: band, method: "text-fallback");
                }
                else
                {
                    SetDecision(context, IdentityDecision.Review, ReviewRootCause.InsufficientEvidence,
                        band: band, method: "none");
                }
                return;
        }
    }

    /// <summary>
    /// Evaluates the Wikidata (Stage 2) outcome and sets
    /// <see cref="IdentityResolutionContext.Decision"/> and
    /// <see cref="IdentityResolutionContext.ReviewCause"/> accordingly.
    ///
    /// Decision rules:
    /// <list type="bullet">
    ///   <item>QID resolved via bridge or album → <see cref="IdentityDecision.Accept"/></item>
    ///   <item>QID resolved via text → <see cref="IdentityDecision.ProvisionalAccept"/></item>
    ///   <item>No QID → <see cref="IdentityDecision.Review"/> (<see cref="ReviewRootCause.NoCanonicalIdentity"/>)</item>
    /// </list>
    /// </summary>
    public void EvaluateWikidataOutcome(IdentityResolutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrEmpty(context.ResolvedQid))
        {
            SetDecision(context, IdentityDecision.Review, ReviewRootCause.NoCanonicalIdentity,
                band: context.RetailBand, method: "none");
            return;
        }

        var method = context.ResolutionMethod ?? string.Empty;

        if (method is "bridge" or "album")
        {
            SetDecision(context, IdentityDecision.Accept, rootCause: null,
                band: context.RetailBand, method: method);
        }
        else
        {
            // "text" resolution — lower confidence, flag provisionally.
            SetDecision(context, IdentityDecision.ProvisionalAccept, rootCause: null,
                band: context.RetailBand, method: method);
        }
    }

    /// <summary>
    /// Returns <c>true</c> when the item has enough resolved metadata to be
    /// moved from staging into the organised library.
    ///
    /// Gate: <see cref="IdentityResolutionContext.CanonicalValues"/> must contain
    /// a non-empty, non-placeholder title (not "Untitled" or "Unknown").
    /// Title is the minimum requirement for filesystem organisation.
    /// </summary>
    public bool EvaluateOrganizationReadiness(IdentityResolutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.CanonicalValues.TryGetValue(MetadataFieldConstants.Title, out var title))
            return false;

        if (string.IsNullOrWhiteSpace(title))
            return false;

        if (PlaceholderTitles.Contains(title.Trim()))
            return false;

        return true;
    }

    /// <summary>
    /// Returns <c>true</c> when text-only Wikidata CirrusSearch fallback
    /// is allowed for this item.
    ///
    /// Requirements (all must pass):
    /// <list type="number">
    ///   <item><see cref="IMediaTypeIdentityStrategy.AllowsTextFallback"/> is <c>true</c></item>
    ///   <item><see cref="IdentityResolutionContext.MediaType"/> is not <see cref="MediaType.Unknown"/></item>
    ///   <item>A title claim with confidence ≥ <see cref="IMediaTypeIdentityStrategy.TextFallbackMinConfidence"/> exists in <see cref="IdentityResolutionContext.FileMetadataClaims"/></item>
    ///   <item>When <see cref="IMediaTypeIdentityStrategy.RequiresCreatorForFallback"/> is <c>true</c>: a creator claim (author/artist/director) with confidence ≥ threshold also exists</item>
    /// </list>
    /// </summary>
    public bool EvaluateTextFallbackEligibility(IdentityResolutionContext context, IMediaTypeIdentityStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(strategy);

        if (!strategy.AllowsTextFallback)
            return false;

        if (context.MediaType == MediaType.Unknown)
            return false;

        var minConfidence = strategy.TextFallbackMinConfidence;

        // Title claim must meet the threshold.
        var hasTitleClaim = context.FileMetadataClaims.Any(c =>
            string.Equals(c.ClaimKey, MetadataFieldConstants.Title, StringComparison.OrdinalIgnoreCase)
            && c.Confidence >= minConfidence);

        if (!hasTitleClaim)
            return false;

        // Creator claim required when the strategy demands it.
        if (strategy.RequiresCreatorForFallback)
        {
            var hasCreatorClaim = context.FileMetadataClaims.Any(c =>
                CreatorClaimKeys.Any(key =>
                    string.Equals(c.ClaimKey, key, StringComparison.OrdinalIgnoreCase))
                && c.Confidence >= minConfidence);

            if (!hasCreatorClaim)
                return false;
        }

        return true;
    }

    // ── Strategy access ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns the registered strategy for the given media type,
    /// or <c>null</c> when the type is <see cref="MediaType.Unknown"/> or
    /// no strategy has been registered.
    /// </summary>
    public IMediaTypeIdentityStrategy? GetStrategy(MediaType type)
    {
        if (type == MediaType.Unknown)
            return null;

        return _strategies.GetValueOrDefault(type);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void SetDecision(
        IdentityResolutionContext context,
        IdentityDecision decision,
        ReviewRootCause? rootCause,
        string band,
        string method)
    {
        context.Decision    = decision;
        context.ReviewCause = decision == IdentityDecision.Review ? rootCause : null;

        _logger.LogInformation(
            "Identity decision for {EntityId}: {Decision} (retail band: {Band}, resolution: {Method})",
            context.EntityId, decision, band, method);
    }
}
