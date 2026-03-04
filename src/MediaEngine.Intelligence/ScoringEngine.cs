using MediaEngine.Domain.Entities;
using MediaEngine.Intelligence.Contracts;
using MediaEngine.Intelligence.Models;

namespace MediaEngine.Intelligence;

/// <summary>
/// Provider-agnostic engine that scores all metadata claims for an entity
/// and produces a <see cref="ScoringResult"/> ready for Hub arbitration.
///
/// ──────────────────────────────────────────────────────────────────
/// Processing pipeline (spec: Phase 6 / Phase 8 – Claim Arbitration)
/// ──────────────────────────────────────────────────────────────────
///  For each entity:
///  1. Group claims by <c>ClaimKey</c>.
///  2. For each field-group:
///     a. If any claim is UserLocked → that claim wins unconditionally (confidence 1.0).
///     b. Otherwise: build effective per-provider weights by merging the global
///        ProviderWeights with any per-field overrides from ProviderFieldWeights,
///        then delegate to <see cref="IConflictResolver.Resolve"/>.
///  3. Map each resolution to a <see cref="FieldScore"/>.
///  4. Compute <see cref="ScoringResult.OverallConfidence"/> = mean of per-field
///     confidences.
///
/// ──────────────────────────────────────────────────────────────────
/// User-Locked Claims (spec: Phase 8 – Field-Level Arbitration)
/// ──────────────────────────────────────────────────────────────────
///  A <see cref="MetadataClaim"/> with <c>IsUserLocked = true</c> wins
///  unconditionally for its field.  The scoring engine short-circuits before
///  the ConflictResolver runs.  If multiple locked claims exist for the same
///  field (edge case), the most recently asserted one wins.
///
/// ──────────────────────────────────────────────────────────────────
/// Field-Level Weight Matrix (spec: Phase 8 – Field-Level Arbitration)
/// ──────────────────────────────────────────────────────────────────
///  When <see cref="ScoringContext.ProviderFieldWeights"/> is present, the engine
///  builds a field-specific effective-weight dictionary before each resolver call:
///    effectiveWeight(providerId) =
///        ProviderFieldWeights[providerId][fieldKey]   (if present)
///        ?? ProviderWeights[providerId]               (global fallback)
///        ?? 1.0                                       (absolute fallback)
///
///  The <see cref="IConflictResolver"/> signature is unchanged — it always
///  receives a flat <c>IReadOnlyDictionary&lt;Guid, double&gt;</c>.
///
/// ──────────────────────────────────────────────────────────────────
/// Conflict isolation (spec: Phase 6 – Conflict Isolation invariant)
/// ──────────────────────────────────────────────────────────────────
///  Each field is scored independently; an exception on one field is caught,
///  the field is skipped, and scoring continues for remaining fields.
///
/// ──────────────────────────────────────────────────────────────────
/// Batch scoring
/// ──────────────────────────────────────────────────────────────────
///  <see cref="ScoreBatchAsync"/> fans out to <see cref="ScoreEntityAsync"/>
///  using <c>Task.WhenAll</c> — the caller should supply a reasonable number
///  of contexts to avoid excessive parallelism.
/// </summary>
public sealed class ScoringEngine : IScoringEngine
{
    private readonly IConflictResolver _resolver;

    public ScoringEngine(IConflictResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _resolver = resolver;
    }

    // -------------------------------------------------------------------------
    // IScoringEngine
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public Task<ScoringResult> ScoreEntityAsync(ScoringContext context, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(context);

        var fieldScores = new List<FieldScore>();

        // Group claims by field key, then resolve each group independently.
        var groups = context.Claims
            .GroupBy(c => c.ClaimKey, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();

            var claimsForField = group.ToList();
            if (claimsForField.Count == 0) continue;

            // ── A: User-Locked short-circuit ──────────────────────────────
            // A claim set by the user (IsUserLocked) wins unconditionally at
            // confidence 1.0.  The ConflictResolver is never invoked for this
            // field.  If multiple locked claims exist, the most recent wins.
            // Spec: Phase 8 – Field-Level Arbitration § User-Locked Claims.
            var lockedClaims = claimsForField
                .Where(c => c.IsUserLocked)
                .ToList();

            if (lockedClaims.Count > 0)
            {
                // Pick the most recently asserted locked claim.
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

            // ── B: Field-weight pre-computation ───────────────────────────
            // Build effective weights for this specific field by merging the
            // global ProviderWeights with per-field overrides from
            // ProviderFieldWeights (when present).
            // Spec: Phase 8 – Field-Level Weight Matrix.
            IReadOnlyDictionary<Guid, double> effectiveWeights =
                BuildEffectiveWeights(group.Key, context);

            // ── C: Conflict resolution ────────────────────────────────────
            // Spec: "A failure to score one specific field MUST NOT prevent
            //        the scoring of other fields within the same entity."
            ClaimResolution resolution;
            try
            {
                resolution = _resolver.Resolve(
                    group.Key,
                    claimsForField,
                    effectiveWeights,
                    context.Configuration);
            }
            catch (Exception)
            {
                // Log could be added here; for now we skip this field silently.
                continue;
            }

            fieldScores.Add(new FieldScore
            {
                Key               = group.Key,
                WinningValue      = resolution.WinningClaim.ClaimValue,
                Confidence        = resolution.AdjustedConfidence,
                WinningProviderId = resolution.WinningClaim.ProviderId,
                IsConflicted      = resolution.IsConflicted,
            });
        }

        double overallConfidence = fieldScores.Count > 0
            ? fieldScores.Average(f => f.Confidence)
            : 0.0;

        var result = new ScoringResult
        {
            EntityId          = context.EntityId,
            FieldScores       = fieldScores,
            OverallConfidence = overallConfidence,
            ScoredAt          = DateTimeOffset.UtcNow,
        };

        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ScoringResult>> ScoreBatchAsync(
        IEnumerable<ScoringContext> contexts,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(contexts);

        var tasks = contexts
            .Select(ctx => ScoreEntityAsync(ctx, ct));

        ScoringResult[] results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds the effective per-provider weight dictionary for a specific
    /// <paramref name="fieldKey"/>.
    ///
    /// Resolution order (first match wins):
    ///   1. <c>context.ProviderFieldWeights[providerId][fieldKey]</c> — field-specific override
    ///   2. <c>context.ProviderWeights[providerId]</c>               — global provider weight
    ///   3. 1.0                                                       — absolute default
    ///
    /// The returned dictionary covers only providers that appear in either weight
    /// source; <see cref="IConflictResolver"/> applies its own 1.0 default for
    /// providers absent from the map.
    /// </summary>
    private static IReadOnlyDictionary<Guid, double> BuildEffectiveWeights(
        string fieldKey,
        ScoringContext context)
    {
        // Fast path: no field-specific overrides — return global weights as-is.
        if (context.ProviderFieldWeights is null)
            return context.ProviderWeights;

        // Union of all provider IDs known via either weight source.
        var allProviderIds = context.ProviderWeights.Keys
            .Union(context.ProviderFieldWeights.Keys)
            .ToHashSet();

        var effective = new Dictionary<Guid, double>(allProviderIds.Count);
        foreach (var pid in allProviderIds)
        {
            // Field-specific override takes precedence over global weight.
            if (context.ProviderFieldWeights.TryGetValue(pid, out var fieldMap)
                && fieldMap.TryGetValue(fieldKey, out double fieldWeight))
            {
                effective[pid] = fieldWeight;
            }
            else if (context.ProviderWeights.TryGetValue(pid, out double globalWeight))
            {
                effective[pid] = globalWeight;
            }
            else
            {
                effective[pid] = 1.0;
            }
        }

        return effective;
    }
}
