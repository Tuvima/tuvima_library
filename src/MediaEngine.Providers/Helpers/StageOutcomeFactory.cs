using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Helpers;

/// <summary>
/// Creates review items with correct triggers and detail text.
/// Centralizes trigger selection so no worker can accidentally use the wrong trigger.
/// </summary>
public sealed class StageOutcomeFactory
{
    private readonly IReviewQueueRepository _reviewRepo;
    private readonly ISystemActivityRepository _activityRepo;
    private readonly IEventPublisher _eventPublisher;
    private readonly ICanonicalValueRepository _canonicalRepo;
    private readonly ILogger<StageOutcomeFactory> _logger;

    public StageOutcomeFactory(
        IReviewQueueRepository reviewRepo,
        ISystemActivityRepository activityRepo,
        IEventPublisher eventPublisher,
        ICanonicalValueRepository canonicalRepo,
        ILogger<StageOutcomeFactory> logger)
    {
        _reviewRepo    = reviewRepo;
        _activityRepo  = activityRepo;
        _eventPublisher = eventPublisher;
        _canonicalRepo = canonicalRepo;
        _logger        = logger;
    }

    /// <summary>
    /// Creates a <see cref="ReviewTrigger.RetailMatchFailed"/> review item when
    /// no retail provider returned a match for the entity.
    /// </summary>
    /// <param name="entityId">The entity that failed retail matching.</param>
    /// <param name="mediaType">The media type label used in the detail message.</param>
    /// <param name="ingestionRunId">Optional ingestion run for activity correlation.</param>
    /// <param name="onBatchAdjust">Optional callback to shift batch counters (receives the ingestion run ID).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The review entry ID, or <c>null</c> if a duplicate pending review already exists.</returns>
    public Task<Guid?> CreateRetailFailedAsync(
        Guid entityId,
        string mediaType,
        Guid? ingestionRunId = null,
        Action<Guid?>? onBatchAdjust = null,
        CancellationToken ct = default)
    {
        return CreateCoreAsync(
            entityId,
            ReviewTrigger.RetailMatchFailed,
            0.0,
            $"Retail identification failed for this {mediaType} \u2014 no provider returned a match",
            ingestionRunId,
            onBatchAdjust,
            ct);
    }

    /// <summary>
    /// Creates a <see cref="ReviewTrigger.RetailMatchAmbiguous"/> review item when
    /// the top retail candidate scored between the ambiguous and auto-accept thresholds.
    /// </summary>
    /// <param name="entityId">The entity with an ambiguous retail match.</param>
    /// <param name="mediaType">The media type label used in the detail message.</param>
    /// <param name="score">The retail match confidence score.</param>
    /// <param name="ingestionRunId">Optional ingestion run for activity correlation.</param>
    /// <param name="onBatchAdjust">Optional callback to shift batch counters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The review entry ID, or <c>null</c> if a duplicate pending review already exists.</returns>
    public Task<Guid?> CreateRetailAmbiguousAsync(
        Guid entityId,
        string mediaType,
        double score,
        Guid? ingestionRunId = null,
        Action<Guid?>? onBatchAdjust = null,
        CancellationToken ct = default)
    {
        return CreateCoreAsync(
            entityId,
            ReviewTrigger.RetailMatchAmbiguous,
            score,
            $"Retail match found with confidence {score:P0} \u2014 needs confirmation",
            ingestionRunId,
            onBatchAdjust,
            ct);
    }

    /// <summary>
    /// Creates a <see cref="ReviewTrigger.WikidataBridgeFailed"/> review item when
    /// Stage 2 bridge resolution could not find a Wikidata entity.
    /// </summary>
    /// <param name="entityId">The entity that failed Wikidata bridge resolution.</param>
    /// <param name="detail">Human-readable detail explaining the failure.</param>
    /// <param name="ingestionRunId">Optional ingestion run for activity correlation.</param>
    /// <param name="onBatchAdjust">Optional callback to shift batch counters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The review entry ID, or <c>null</c> if a duplicate pending review already exists.</returns>
    public Task<Guid?> CreateWikidataBridgeFailedAsync(
        Guid entityId,
        string detail,
        Guid? ingestionRunId = null,
        Action<Guid?>? onBatchAdjust = null,
        CancellationToken ct = default)
    {
        return CreateCoreAsync(
            entityId,
            ReviewTrigger.WikidataBridgeFailed,
            0.0,
            detail,
            ingestionRunId,
            onBatchAdjust,
            ct);
    }

    /// <summary>
    /// Creates a <see cref="ReviewTrigger.MultipleQidMatches"/> review item when
    /// multiple Wikidata QID candidates were returned and could not be disambiguated.
    /// </summary>
    /// <param name="entityId">The entity with ambiguous QID candidates.</param>
    /// <param name="candidatesJson">Serialized JSON array of QID candidates.</param>
    /// <param name="ingestionRunId">Optional ingestion run for activity correlation.</param>
    /// <param name="onBatchAdjust">Optional callback to shift batch counters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The review entry ID, or <c>null</c> if a duplicate pending review already exists.</returns>
    public async Task<Guid?> CreateMultipleQidMatchesAsync(
        Guid entityId,
        string candidatesJson,
        Guid? ingestionRunId = null,
        Action<Guid?>? onBatchAdjust = null,
        CancellationToken ct = default)
    {
        // Dedup check.
        var existing = await _reviewRepo.GetByEntityAsync(entityId, ct).ConfigureAwait(false);
        if (existing.Any(r => r.Status == ReviewStatus.Pending
                              && r.Trigger == ReviewTrigger.MultipleQidMatches))
        {
            _logger.LogDebug(
                "Review item '{Trigger}' already exists for entity {Id} \u2014 skipping duplicate",
                ReviewTrigger.MultipleQidMatches, entityId);
            return null;
        }

        var entry = new ReviewQueueEntry
        {
            Id              = Guid.NewGuid(),
            EntityId        = entityId,
            EntityType      = "Work",
            Trigger         = ReviewTrigger.MultipleQidMatches,
            ConfidenceScore = 0.0,
            Detail          = $"Multiple Wikidata QID candidates found \u2014 manual disambiguation required",
            CandidatesJson  = candidatesJson,
        };

        await _reviewRepo.InsertAsync(entry, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Pipeline: entity {EntityId} sent to review \u2014 trigger={Trigger}",
            entityId, ReviewTrigger.MultipleQidMatches);

        onBatchAdjust?.Invoke(ingestionRunId);

        await LogActivityAndPublishAsync(entry, ingestionRunId, ct).ConfigureAwait(false);

        return entry.Id;
    }

    /// <summary>
    /// Creates a <see cref="ReviewTrigger.LowConfidence"/> review item when
    /// the entity's overall confidence falls below the auto-review threshold.
    /// </summary>
    /// <param name="entityId">The low-confidence entity.</param>
    /// <param name="confidence">The entity's overall confidence score.</param>
    /// <param name="ingestionRunId">Optional ingestion run for activity correlation.</param>
    /// <param name="onBatchAdjust">Optional callback to shift batch counters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The review entry ID, or <c>null</c> if a duplicate pending review already exists.</returns>
    public Task<Guid?> CreateLowConfidenceAsync(
        Guid entityId,
        double confidence,
        Guid? ingestionRunId = null,
        Action<Guid?>? onBatchAdjust = null,
        CancellationToken ct = default)
    {
        return CreateCoreAsync(
            entityId,
            ReviewTrigger.LowConfidence,
            confidence,
            $"Overall confidence {confidence:P0} below auto-accept threshold",
            ingestionRunId,
            onBatchAdjust,
            ct);
    }

    // ── Private ──────────────────────────────────────────────────────────

    /// <summary>
    /// Shared implementation: dedup check, insert, log activity, publish event.
    /// </summary>
    private async Task<Guid?> CreateCoreAsync(
        Guid entityId,
        string trigger,
        double confidence,
        string detail,
        Guid? ingestionRunId,
        Action<Guid?>? onBatchAdjust,
        CancellationToken ct)
    {
        // Dedup: skip if a pending review with the same trigger already exists.
        var existing = await _reviewRepo.GetByEntityAsync(entityId, ct).ConfigureAwait(false);
        if (existing.Any(r => r.Status == ReviewStatus.Pending && r.Trigger == trigger))
        {
            _logger.LogDebug(
                "Review item '{Trigger}' already exists for entity {Id} \u2014 skipping duplicate",
                trigger, entityId);
            return null;
        }

        var entry = new ReviewQueueEntry
        {
            Id              = Guid.NewGuid(),
            EntityId        = entityId,
            EntityType      = "Work",
            Trigger         = trigger,
            ConfidenceScore = confidence,
            Detail          = detail,
        };

        await _reviewRepo.InsertAsync(entry, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Pipeline: entity {EntityId} sent to review \u2014 trigger={Trigger}, confidence={Score:P0}",
            entityId, trigger, confidence);

        onBatchAdjust?.Invoke(ingestionRunId);

        await LogActivityAndPublishAsync(entry, ingestionRunId, ct).ConfigureAwait(false);

        return entry.Id;
    }

    /// <summary>
    /// Logs a <see cref="SystemActionType.ReviewItemCreated"/> activity entry and
    /// publishes the <see cref="SignalREvents.ReviewItemCreated"/> event.
    /// </summary>
    private async Task LogActivityAndPublishAsync(
        ReviewQueueEntry entry,
        Guid? ingestionRunId,
        CancellationToken ct)
    {
        await _activityRepo.LogAsync(new SystemActivityEntry
        {
            ActionType     = SystemActionType.ReviewItemCreated,
            EntityId       = entry.EntityId,
            Detail         = $"Review item created: {entry.Trigger}",
            IngestionRunId = ingestionRunId,
        }, ct).ConfigureAwait(false);

        // Resolve title for the SignalR event payload.
        string? titleText = null;
        try
        {
            var canonicals = await _canonicalRepo.GetByEntityAsync(entry.EntityId, ct)
                .ConfigureAwait(false);
            titleText = canonicals
                .FirstOrDefault(c => c.Key == MetadataFieldConstants.Title)?.Value;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not resolve title for review event {ReviewId}", entry.Id);
        }

        await _eventPublisher.PublishAsync(
            SignalREvents.ReviewItemCreated,
            new ReviewItemCreatedEvent(entry.Id, entry.EntityId, entry.Trigger, titleText),
            ct).ConfigureAwait(false);
    }
}
