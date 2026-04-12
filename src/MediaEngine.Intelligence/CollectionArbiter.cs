using MediaEngine.Domain.Aggregates;
using MediaEngine.Intelligence.Contracts;
using MediaEngine.Intelligence.Models;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Intelligence;

/// <summary>
/// Evaluates a Work against a set of Collection candidates and decides whether to
/// automatically link it, flag it for review, or reject all candidates.
///
/// ──────────────────────────────────────────────────────────────────
/// Evaluation algorithm (spec: Phase 6 – Collection Clustering)
/// ──────────────────────────────────────────────────────────────────
///  For each candidate Collection:
///   • Skip if the Work already belongs to that Collection (circular-link guard).
///   • Run <see cref="IIdentityMatcher.MatchAsync"/> against each Work in the Collection.
///   • Collection score = best Work-level match score within the Collection.
///
///  After all Collections are evaluated:
///   • Select the Collection with the highest score.
///   • Apply threshold rules to determine <see cref="LinkDisposition"/>.
///   • Write the decision to <c>transaction_log</c>.
///   • Return the <see cref="ArbiterDecision"/>.
///
/// ──────────────────────────────────────────────────────────────────
/// Transaction log events (spec: Phase 6 – Failure Handling)
/// ──────────────────────────────────────────────────────────────────
///  WORK_AUTO_LINKED   — score ≥ auto_link_threshold
///  WORK_NEEDS_REVIEW  — score ∈ [conflict_threshold, auto_link_threshold)
///  WORK_LINK_REJECTED — score &lt; conflict_threshold (or no candidates)
///
/// ──────────────────────────────────────────────────────────────────
/// Non-goals (spec: Phase 6 – Non-Goals)
/// ──────────────────────────────────────────────────────────────────
///  • This class MUST NOT create new Collections.
///  • This class MUST NOT modify the Work or Collection objects.
/// </summary>
public sealed class CollectionArbiter : ICollectionArbiter
{
    private readonly IIdentityMatcher   _matcher;
    private readonly ITransactionJournal _journal;

    public CollectionArbiter(IIdentityMatcher matcher, ITransactionJournal journal)
    {
        ArgumentNullException.ThrowIfNull(matcher);
        ArgumentNullException.ThrowIfNull(journal);
        _matcher = matcher;
        _journal = journal;
    }

    // -------------------------------------------------------------------------
    // ICollectionArbiter
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<ArbiterDecision> EvaluateAsync(
        Work work,
        IEnumerable<Collection> collectionCandidates,
        IReadOnlyDictionary<Guid, double> providerWeights,
        ScoringConfiguration configuration,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(collectionCandidates);
        ArgumentNullException.ThrowIfNull(providerWeights);
        ArgumentNullException.ThrowIfNull(configuration);

        ct.ThrowIfCancellationRequested();

        // Work must have CanonicalValues populated before arbitration.
        var workCanonical = work.CanonicalValues;

        double bestScore = 0.0;
        Guid   bestCollection   = Guid.Empty;
        string bestReason = "No Collection candidates evaluated.";

        foreach (var collection in collectionCandidates)
        {
            ct.ThrowIfCancellationRequested();

            // ── Circular-link guard ──────────────────────────────────────
            // Skip if the Work already belongs to this Collection — no change needed.
            if (work.CollectionId == collection.Id) continue;

            // ── Score against each Work within the Collection ───────────────────
            foreach (var collectionWork in collection.Works)
            {
                if (collectionWork.Id == work.Id) continue;   // skip self

                var matchResult = await _matcher.MatchAsync(
                    workCanonical,
                    collectionWork.CanonicalValues,
                    configuration,
                    ct).ConfigureAwait(false);

                if (matchResult.Similarity > bestScore)
                {
                    bestScore  = matchResult.Similarity;
                    bestCollection    = collection.Id;
                    bestReason = BuildReason(matchResult, collection.Id, configuration);
                }
            }
        }

        // ── Apply threshold logic ────────────────────────────────────────
        var disposition = DetermineDisposition(bestScore, configuration);
        var eventType   = DispositionToEventType(disposition);

        var decision = new ArbiterDecision
        {
            WorkId      = work.Id,
            CollectionId       = disposition == LinkDisposition.Rejected ? Guid.Empty : bestCollection,
            Score       = bestScore,
            Disposition = disposition,
            Reason      = bestReason,
            DecidedAt   = DateTimeOffset.UtcNow,
        };

        // ── Write to transaction log (spec: Phase 6 §  Failure Handling) ──
        _journal.Log(
            eventType:  eventType,
            entityType: "Work",
            entityId:   work.Id.ToString());

        return decision;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static LinkDisposition DetermineDisposition(double score, ScoringConfiguration config)
    {
        if (score >= config.AutoLinkThreshold) return LinkDisposition.AutoLinked;
        if (score >= config.ConflictThreshold) return LinkDisposition.NeedsReview;
        return LinkDisposition.Rejected;
    }

    private static string DispositionToEventType(LinkDisposition disposition) => disposition switch
    {
        LinkDisposition.AutoLinked  => "WORK_AUTO_LINKED",
        LinkDisposition.NeedsReview => "WORK_NEEDS_REVIEW",
        _                           => "WORK_LINK_REJECTED",
    };

    private static string BuildReason(
        MatchResult match,
        Guid collectionId,
        ScoringConfiguration config)
    {
        string collectionHex = collectionId.ToString()[..8];   // first 8 chars for readability
        if (match.HardIdentifierMatch)
        {
            string ids = string.Join(", ", match.MatchedIdentifiers);
            return $"Hard-identifier match ({ids}) → Collection {collectionHex}; score 1.0 ≥ threshold {config.AutoLinkThreshold}.";
        }

        string disposition = match.Disposition switch
        {
            LinkDisposition.AutoLinked  => $"Auto-linked: score {match.Similarity:F3} ≥ {config.AutoLinkThreshold}",
            LinkDisposition.NeedsReview => $"Needs review: score {match.Similarity:F3} ∈ [{config.ConflictThreshold}, {config.AutoLinkThreshold})",
            _                           => $"Rejected: score {match.Similarity:F3} < {config.ConflictThreshold}",
        };
        return $"{disposition} → Collection {collectionHex}.";
    }
}
