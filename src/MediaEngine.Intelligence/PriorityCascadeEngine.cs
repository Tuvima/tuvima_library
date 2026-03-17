using MediaEngine.Domain.Enums;
using MediaEngine.Intelligence.Contracts;
using MediaEngine.Intelligence.Models;
using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Models;

namespace MediaEngine.Intelligence;

/// <summary>
/// Replaces the Weighted Voter (<see cref="ScoringEngine"/>) with a simple
/// priority cascade. Wikidata is the sole identity authority — its claims
/// win unconditionally for most fields. Per-field provider priority overrides
/// (loaded from <c>config/field_priorities.json</c>) allow specific fields
/// (description, cover, biography, rating) to prefer other providers.
/// User edits are accepted only for empty fields (no Wikidata value) and
/// image fields.
/// </summary>
public sealed class PriorityCascadeEngine : IScoringEngine
{
    /// <summary>
    /// The Wikidata Reconciliation provider GUID.
    /// Claims from this provider always win over retail/local claims
    /// — unless the field has a per-field priority override.
    /// </summary>
    private static readonly Guid WikidataProviderId =
        Guid.Parse("b3000003-d000-4000-8000-000000000004");

    /// <summary>
    /// Per-field provider priority overrides.
    /// Fields listed here use provider-priority ordering instead of Wikidata-always-wins.
    /// </summary>
    private readonly FieldPriorityConfiguration _fieldPriorities;

    /// <summary>
    /// Maps provider name (e.g. "wikipedia") to provider GUID for field priority resolution.
    /// Built from loaded provider configs at construction time.
    /// </summary>
    private readonly IReadOnlyDictionary<string, Guid> _providerNameToGuid;

    public PriorityCascadeEngine(IConfigurationLoader configLoader)
    {
        ArgumentNullException.ThrowIfNull(configLoader);

        _fieldPriorities = configLoader.LoadFieldPriorities();

        // Build provider name → GUID map from all provider configs.
        var nameMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in configLoader.LoadAllProviders())
        {
            if (!string.IsNullOrEmpty(provider.Name) &&
                Guid.TryParse(provider.ProviderId, out var guid))
            {
                nameMap[provider.Name] = guid;
            }
        }
        _providerNameToGuid = nameMap;
    }

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

            var lockedClaims = claimsForField
                .Where(c => c.IsUserLocked)
                .ToList();

            // ── User-Locked claims always win (highest priority) ─────────
            if (lockedClaims.Count > 0)
            {
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

            // ── Check for per-field provider priority override ───────────
            if (_fieldPriorities.FieldOverrides.TryGetValue(group.Key, out var fieldOverride)
                && fieldOverride.Priority.Count > 0)
            {
                var resolved = ResolveByFieldPriority(claimsForField, fieldOverride);
                if (resolved is not null)
                {
                    fieldScores.Add(resolved);
                    continue;
                }
                // No provider from the priority list had a claim — fall through
                // to the standard cascade below.
            }

            // ── A: Wikidata authority (default for non-overridden fields) ─
            var wikidataClaim = claimsForField
                .Where(c => c.ProviderId == WikidataProviderId)
                .OrderByDescending(c => c.ClaimedAt)
                .FirstOrDefault();

            if (wikidataClaim is not null)
            {
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

            // ── B: Priority cascade (no Wikidata, no override) ───────────
            // Pick the highest-confidence claim.
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

        // ── Field count scaling ────────────────────────────────────────
        // Files with very few fields (e.g. only a filename-derived title)
        // should not score high. Scale confidence by min(1, fieldCount/3)
        // so a single-field file gets ~1/3 of its raw average.
        if (fieldScores.Count == 1)
        {
            overallConfidence *= 1.0 / 3.0;
        }

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

    /// <summary>
    /// Walks the per-field provider priority list. Returns the first provider's
    /// claim that exists, or <c>null</c> if none of the listed providers have a claim.
    /// </summary>
    private FieldScore? ResolveByFieldPriority(
        List<Domain.Entities.MetadataClaim> claimsForField,
        FieldPriorityOverride fieldOverride)
    {
        foreach (var providerName in fieldOverride.Priority)
        {
            if (!_providerNameToGuid.TryGetValue(providerName, out var providerGuid))
                continue;

            var claim = claimsForField
                .Where(c => c.ProviderId == providerGuid)
                .OrderByDescending(c => c.ClaimedAt)
                .FirstOrDefault();

            if (claim is not null)
            {
                return new FieldScore
                {
                    Key               = claim.ClaimKey,
                    WinningValue      = claim.ClaimValue,
                    Confidence        = claim.Confidence,
                    WinningProviderId = claim.ProviderId,
                    IsConflicted      = false,
                };
            }
        }

        return null;
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
