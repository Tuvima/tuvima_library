using MediaEngine.Domain.Enums;
using MediaEngine.Intelligence.Contracts;
using MediaEngine.Intelligence.Models;

namespace MediaEngine.Intelligence;

/// <summary>
/// Replaces the Weighted Voter (<see cref="ScoringEngine"/>) with a simple
/// priority cascade. Wikidata is the sole identity authority — its claims
/// win unconditionally. Retail providers fill gaps. User edits are accepted
/// only for empty fields (no Wikidata value) and image fields.
/// </summary>
public sealed class PriorityCascadeEngine : IScoringEngine
{
    /// <summary>
    /// The Wikidata Reconciliation provider GUID.
    /// Claims from this provider always win over retail/local claims.
    /// </summary>
    private static readonly Guid WikidataProviderId =
        Guid.Parse("b3000003-d000-4000-8000-000000000004");

    public Task<ScoringResult> ScoreEntityAsync(ScoringContext context, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(context);

        var fieldScores = new List<FieldScore>();

        var groups = context.Claims
            .GroupBy(c => c.ClaimKey, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();

            var claimsForField = group.ToList();
            if (claimsForField.Count == 0) continue;

            // ── A: User-Locked short-circuit ──────────────────────────────
            // User-locked claims win only if Wikidata has no value for this field.
            // If Wikidata provides a value, user locks are superseded (provenance preserved).
            var wikidataClaim = claimsForField
                .Where(c => c.ProviderId == WikidataProviderId)
                .OrderByDescending(c => c.ClaimedAt)
                .FirstOrDefault();

            var lockedClaims = claimsForField
                .Where(c => c.IsUserLocked)
                .ToList();

            if (wikidataClaim is not null)
            {
                // Wikidata value exists — it wins. User locks are superseded.
                fieldScores.Add(new FieldScore
                {
                    Key               = group.Key,
                    WinningValue      = wikidataClaim.ClaimValue,
                    Confidence        = wikidataClaim.Confidence,
                    WinningProviderId = wikidataClaim.ProviderId,
                    IsConflicted      = false,
                });
                continue;
            }

            if (lockedClaims.Count > 0)
            {
                // No Wikidata value — user lock wins.
                var winner = lockedClaims
                    .OrderByDescending(c => c.ClaimedAt)
                    .First();

                fieldScores.Add(new FieldScore
                {
                    Key               = group.Key,
                    WinningValue      = winner.ClaimValue,
                    Confidence        = 1.0,
                    WinningProviderId = winner.ProviderId,
                    IsConflicted      = false,
                });
                continue;
            }

            // ── B: Priority cascade ──────────────────────────────────────
            // No Wikidata, no user lock. Pick the highest-confidence claim.
            var bestClaim = claimsForField
                .OrderByDescending(c => c.Confidence)
                .ThenByDescending(c => c.ClaimedAt)
                .First();

            fieldScores.Add(new FieldScore
            {
                Key               = group.Key,
                WinningValue      = bestClaim.ClaimValue,
                Confidence        = bestClaim.Confidence,
                WinningProviderId = bestClaim.ProviderId,
                IsConflicted      = false,
            });
        }

        double overallConfidence = fieldScores.Count > 0
            ? fieldScores.Average(f => f.Confidence)
            : 0.0;

        // Apply Library Folder category confidence prior (same as before).
        if (context.CategoryConfidencePrior > 0.0)
            overallConfidence = Math.Min(1.0, overallConfidence + context.CategoryConfidencePrior);

        // Apply media-type-aware confidence floor boost (same as before).
        overallConfidence = ApplyConfidenceFloor(
            overallConfidence, fieldScores, context.DetectedMediaType, context.Configuration);

        var result = new ScoringResult
        {
            EntityId          = context.EntityId,
            FieldScores       = fieldScores,
            OverallConfidence = overallConfidence,
            ScoredAt          = DateTimeOffset.UtcNow,
        };

        return Task.FromResult(result);
    }

    public async Task<IReadOnlyList<ScoringResult>> ScoreBatchAsync(
        IEnumerable<ScoringContext> contexts,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(contexts);
        var tasks = contexts.Select(ctx => ScoreEntityAsync(ctx, ct));
        ScoringResult[] results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }

    private static double ApplyConfidenceFloor(
        double overallConfidence,
        IReadOnlyList<FieldScore> fieldScores,
        MediaType detectedMediaType,
        ScoringConfiguration config)
    {
        if (detectedMediaType == MediaType.Unknown)
            return overallConfidence;

        string mediaTypeName = detectedMediaType.ToString();
        if (!config.ConfidenceFloors.TryGetValue(mediaTypeName, out var floor))
            return overallConfidence;

        var fieldLookup = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var fs in fieldScores)
            fieldLookup[fs.Key] = fs.Confidence;

        foreach (var criticalField in floor.CriticalFields)
        {
            if (!fieldLookup.TryGetValue(criticalField, out double score) || score < floor.MinFieldScore)
                return overallConfidence;
        }

        return Math.Min(1.0, overallConfidence + floor.FloorBoost);
    }
}
