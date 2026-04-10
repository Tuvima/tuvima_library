using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Intelligence.Contracts;
using MediaEngine.Intelligence.Models;
using MediaEngine.Providers.Contracts;
using MediaEngine.Storage.Contracts;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Post-pipeline evaluation: re-scores the entity after hydration, auto-resolves
/// stale review items when confidence improves, checks for metadata conflicts,
/// and gates organization (promotion from staging to the organised library).
///
/// Extracted from <c>HydrationPipelineService</c> post-pipeline section.
/// </summary>
public sealed class PostPipelineService
{
    private readonly IMetadataClaimRepository _claimRepo;
    private readonly ICanonicalValueRepository _canonicalRepo;
    private readonly IScoringEngine _scoringEngine;
    private readonly IConfigurationLoader _configLoader;
    private readonly IEnumerable<IExternalMetadataProvider> _providers;
    private readonly IReviewQueueRepository _reviewRepo;
    private readonly IAutoOrganizeService _organizer;
    private readonly ICanonicalValueArrayRepository? _arrayRepo;
    private readonly ISearchIndexRepository? _searchIndex;
    private readonly BatchProgressService _batchProgress;
    private readonly ILogger<PostPipelineService> _logger;

    public PostPipelineService(
        IMetadataClaimRepository claimRepo,
        ICanonicalValueRepository canonicalRepo,
        IScoringEngine scoringEngine,
        IConfigurationLoader configLoader,
        IEnumerable<IExternalMetadataProvider> providers,
        IReviewQueueRepository reviewRepo,
        IAutoOrganizeService organizer,
        BatchProgressService batchProgress,
        ILogger<PostPipelineService> logger,
        ICanonicalValueArrayRepository? arrayRepo = null,
        ISearchIndexRepository? searchIndex = null)
    {
        _claimRepo = claimRepo;
        _canonicalRepo = canonicalRepo;
        _scoringEngine = scoringEngine;
        _configLoader = configLoader;
        _providers = providers;
        _reviewRepo = reviewRepo;
        _organizer = organizer;
        _batchProgress = batchProgress;
        _logger = logger;
        _arrayRepo = arrayRepo;
        _searchIndex = searchIndex;
    }

    /// <summary>
    /// Re-scores the entity, evaluates confidence, auto-resolves stale reviews,
    /// checks for metadata conflicts, and gates organization.
    /// </summary>
    public async Task EvaluateAndOrganizeAsync(
        Guid entityId,
        Guid jobId,
        string? wikidataQid,
        Guid? ingestionRunId,
        CancellationToken ct)
    {
        // 1. Reload all claims and re-score
        var allClaims = await _claimRepo.GetByEntityAsync(entityId, ct);
        var providerConfigs = _configLoader.LoadAllProviders();
        var scoring = _configLoader.LoadScoring();
        var hydration = _configLoader.LoadHydration();

        var (weights, fieldWeights) = ScoringHelper.BuildWeightMaps(providerConfigs, _providers);

        var scoringContext = new ScoringContext
        {
            EntityId = entityId,
            Claims = allClaims,
            ProviderWeights = weights,
            ProviderFieldWeights = fieldWeights,
            Configuration = new Intelligence.Models.ScoringConfiguration
            {
                AutoLinkThreshold = scoring.AutoLinkThreshold,
                ConflictThreshold = scoring.ConflictThreshold,
                ConflictEpsilon = scoring.ConflictEpsilon,
                StaleClaimDecayDays = scoring.StaleClaimDecayDays,
                StaleClaimDecayFactor = scoring.StaleClaimDecayFactor,
            },
        };

        var scored = await _scoringEngine.ScoreEntityAsync(scoringContext, ct);

        // QID-presence boost: a confirmed Wikidata identity is strong evidence
        // of correct identification. Apply a modest boost (+0.10) to overall
        // confidence so enriched-but-sparse items (e.g. video stubs with only
        // title + year) clear the post-hydration organize threshold.
        double effectiveConfidence = scored.OverallConfidence;
        if (wikidataQid is not null && effectiveConfidence < 1.0)
        {
            effectiveConfidence = Math.Min(1.0, effectiveConfidence + 0.10);
            _logger.LogDebug(
                "QID-presence boost for entity {EntityId}: {Raw:F2} → {Boosted:F2} (QID {Qid})",
                entityId, scored.OverallConfidence, effectiveConfidence, wikidataQid);
        }

        _logger.LogInformation(
            "Post-pipeline confidence for entity {EntityId}: {Confidence:F2}",
            entityId, effectiveConfidence);

        // 2. Low confidence → review
        if (effectiveConfidence < hydration.AutoReviewConfidenceThreshold)
        {
            _logger.LogInformation(
                "Entity {EntityId} below confidence threshold ({Confidence:F2} < {Threshold:F2})",
                entityId, effectiveConfidence, hydration.AutoReviewConfidenceThreshold);
            return;
        }

        // 3. Auto-resolve stale review items
        await TryAutoResolveStaleReviewItemsAsync(entityId, effectiveConfidence, ct);
        await TryAutoResolveMetadataConflictsAsync(entityId, effectiveConfidence, ct);

        // 4. Organization gate
        double organizeThreshold = wikidataQid is not null
            ? hydration.PostHydrationOrganizeThreshold
            : scoring.AutoLinkThreshold;

        if (effectiveConfidence >= organizeThreshold)
        {
            _logger.LogInformation(
                "Entity {EntityId} meets organization threshold ({Confidence:F2} >= {Threshold:F2}), promoting",
                entityId, scored.OverallConfidence, organizeThreshold);

            await _organizer.TryAutoOrganizeAsync(entityId, ct, ingestionRunId);
        }
    }

    /// <summary>
    /// Resolves review items that are no longer relevant because confidence has improved.
    /// Handles RetailMatchFailed, AuthorityMatchFailed (legacy), ContentMatchFailed (legacy),
    /// and LowConfidence triggers.
    /// </summary>
    private async Task TryAutoResolveStaleReviewItemsAsync(
        Guid entityId, double confidence, CancellationToken ct)
    {
        var pendingReviews = await _reviewRepo.GetByEntityAsync(entityId, ct);

        var autoResolveTriggers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            nameof(ReviewTrigger.RetailMatchFailed),
            nameof(ReviewTrigger.RetailMatchAmbiguous),
            nameof(ReviewTrigger.AuthorityMatchFailed),
            nameof(ReviewTrigger.ContentMatchFailed),
            nameof(ReviewTrigger.LowConfidence),
            nameof(ReviewTrigger.WikidataBridgeFailed),
            nameof(ReviewTrigger.MissingQid),
            nameof(ReviewTrigger.WritebackFailed),
        };

        foreach (var review in pendingReviews)
        {
            if (review.Status != "Pending") continue;
            if (!autoResolveTriggers.Contains(review.Trigger)) continue;

            _logger.LogInformation(
                "Auto-resolving {Trigger} review for entity {EntityId} (confidence: {Confidence:F2})",
                review.Trigger, entityId, confidence);

            await _reviewRepo.UpdateStatusAsync(
                review.Id,
                ReviewStatus.Resolved,
                "system:post-pipeline",
                ct);
        }
    }

    /// <summary>
    /// Resolves MetadataConflict reviews when no conflicts remain in the scored canonicals.
    /// </summary>
    private async Task TryAutoResolveMetadataConflictsAsync(
        Guid entityId, double confidence, CancellationToken ct)
    {
        var pendingReviews = await _reviewRepo.GetByEntityAsync(entityId, ct);

        foreach (var review in pendingReviews)
        {
            if (review.Status != "Pending") continue;
            if (!string.Equals(review.Trigger, nameof(ReviewTrigger.MetadataConflict),
                    StringComparison.OrdinalIgnoreCase))
                continue;

            // Re-check: if confidence is high enough, the conflict is likely resolved
            if (confidence >= 0.80)
            {
                _logger.LogInformation(
                    "Auto-resolving MetadataConflict review for entity {EntityId} (confidence: {Confidence:F2})",
                    entityId, confidence);

                await _reviewRepo.UpdateStatusAsync(
                    review.Id,
                    ReviewStatus.Resolved,
                    "system:post-pipeline",
                    ct);
            }
        }
    }
}
