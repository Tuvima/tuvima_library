using MediaEngine.Domain.Aggregates;
using MediaEngine.Intelligence.Contracts;
using MediaEngine.Intelligence.Models;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Intelligence;

/// <summary>
/// Evaluates a Work against a set of Hub candidates and decides whether to
/// automatically link it, flag it for review, or reject all candidates.
///
/// ──────────────────────────────────────────────────────────────────
/// Evaluation algorithm (spec: Phase 6 – Hub Clustering)
/// ──────────────────────────────────────────────────────────────────
///  For each candidate Hub:
///   • Skip if the Work already belongs to that Hub (circular-link guard).
///   • Run <see cref="IIdentityMatcher.MatchAsync"/> against each Work in the Hub.
///   • Hub score = best Work-level match score within the Hub.
///
///  After all Hubs are evaluated:
///   • Select the Hub with the highest score.
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
///  • This class MUST NOT create new Hubs.
///  • This class MUST NOT modify the Work or Hub objects.
/// </summary>
public sealed class HubArbiter : IHubArbiter
{
    private readonly IIdentityMatcher   _matcher;
    private readonly ITransactionJournal _journal;

    public HubArbiter(IIdentityMatcher matcher, ITransactionJournal journal)
    {
        ArgumentNullException.ThrowIfNull(matcher);
        ArgumentNullException.ThrowIfNull(journal);
        _matcher = matcher;
        _journal = journal;
    }

    // -------------------------------------------------------------------------
    // IHubArbiter
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<ArbiterDecision> EvaluateAsync(
        Work work,
        IEnumerable<Hub> hubCandidates,
        IReadOnlyDictionary<Guid, double> providerWeights,
        ScoringConfiguration configuration,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(hubCandidates);
        ArgumentNullException.ThrowIfNull(providerWeights);
        ArgumentNullException.ThrowIfNull(configuration);

        ct.ThrowIfCancellationRequested();

        // Work must have CanonicalValues populated before arbitration.
        var workCanonical = work.CanonicalValues;

        double bestScore = 0.0;
        Guid   bestHub   = Guid.Empty;
        string bestReason = "No Hub candidates evaluated.";

        foreach (var hub in hubCandidates)
        {
            ct.ThrowIfCancellationRequested();

            // ── Circular-link guard ──────────────────────────────────────
            // Skip if the Work already belongs to this Hub — no change needed.
            if (work.HubId == hub.Id) continue;

            // ── Score against each Work within the Hub ───────────────────
            foreach (var hubWork in hub.Works)
            {
                if (hubWork.Id == work.Id) continue;   // skip self

                var matchResult = await _matcher.MatchAsync(
                    workCanonical,
                    hubWork.CanonicalValues,
                    configuration,
                    ct).ConfigureAwait(false);

                if (matchResult.Similarity > bestScore)
                {
                    bestScore  = matchResult.Similarity;
                    bestHub    = hub.Id;
                    bestReason = BuildReason(matchResult, hub.Id, configuration);
                }
            }
        }

        // ── Apply threshold logic ────────────────────────────────────────
        var disposition = DetermineDisposition(bestScore, configuration);
        var eventType   = DispositionToEventType(disposition);

        var decision = new ArbiterDecision
        {
            WorkId      = work.Id,
            HubId       = disposition == LinkDisposition.Rejected ? Guid.Empty : bestHub,
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
        Guid hubId,
        ScoringConfiguration config)
    {
        string hubHex = hubId.ToString()[..8];   // first 8 chars for readability
        if (match.HardIdentifierMatch)
        {
            string ids = string.Join(", ", match.MatchedIdentifiers);
            return $"Hard-identifier match ({ids}) → Hub {hubHex}; score 1.0 ≥ threshold {config.AutoLinkThreshold}.";
        }

        string disposition = match.Disposition switch
        {
            LinkDisposition.AutoLinked  => $"Auto-linked: score {match.Similarity:F3} ≥ {config.AutoLinkThreshold}",
            LinkDisposition.NeedsReview => $"Needs review: score {match.Similarity:F3} ∈ [{config.ConflictThreshold}, {config.AutoLinkThreshold})",
            _                           => $"Rejected: score {match.Similarity:F3} < {config.ConflictThreshold}",
        };
        return $"{disposition} → Hub {hubHex}.";
    }
}
