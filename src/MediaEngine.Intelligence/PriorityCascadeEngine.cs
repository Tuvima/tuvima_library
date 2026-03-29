using MediaEngine.Domain.Enums;
using MediaEngine.Intelligence.Contracts;
using MediaEngine.Intelligence.Models;
using Microsoft.Extensions.Logging;
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
///
/// <para>
/// Field priorities are reloaded on every <see cref="ScoreEntityAsync"/> call so that
/// changes to <c>config/field_priorities.json</c> take effect without a restart.
/// </para>
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
    /// Fields for which a user lock (Tier A) is honoured.
    /// All other fields are resolved entirely by the provider hierarchy (Tiers B/C/D).
    /// Rationale: structured metadata (title, author, year, genre, etc.) must come
    /// from authoritative providers, not manual overrides, to preserve data integrity
    /// and Wikidata authority. Users may only contribute personal ratings, media-type
    /// corrections, and custom collection tags.
    /// </summary>
    private static readonly HashSet<string> UserLockableFields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "rating",       // User's personal rating for this title
            "media_type",   // User correction of detected media type
            "custom_tags",  // User-defined collection / labelling tags
        };

    /// <summary>
    /// Configuration loader — used to reload field priorities on every scoring call.
    /// </summary>
    private readonly IConfigurationLoader _configLoader;

    /// <summary>
    /// Maps provider name (e.g. "wikipedia") to provider GUID for field priority resolution.
    /// Built once at construction from the provider configs that exist at startup.
    /// </summary>
    private readonly IReadOnlyDictionary<string, Guid> _providerNameToGuid;

    private readonly ILogger<PriorityCascadeEngine>? _logger;

    public PriorityCascadeEngine(
        IConfigurationLoader configLoader,
        ILogger<PriorityCascadeEngine>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(configLoader);

        _configLoader = configLoader;
        _logger       = logger;

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

        // Log the field priority config at startup so the operator can confirm it loaded.
        var initialPriorities = configLoader.LoadFieldPriorities();
        if (initialPriorities.FieldOverrides.Count == 0)
        {
            _logger?.LogWarning(
                "PriorityCascadeEngine: field_priorities.json not found or empty — " +
                "Wikidata will win for ALL fields including 'description'. " +
                "Wikipedia rich descriptions will be stored as claims but will NOT appear as canonical values. " +
                "Create config/field_priorities.json with a 'description' override to prefer Wikipedia.");
        }
        else
        {
            _logger?.LogInformation(
                "PriorityCascadeEngine: loaded {Count} field priority override(s): [{Fields}]",
                initialPriorities.FieldOverrides.Count,
                string.Join(", ", initialPriorities.FieldOverrides.Keys));
        }
    }

    public Task<ScoringResult> ScoreEntityAsync(ScoringContext context, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(context);

        // Reload field priorities on every call so changes to field_priorities.json
        // take effect without a restart. The file is small and the load is a no-op
        // cache miss (file → parse → return) — negligible cost per scoring call.
        var fieldPriorities = _configLoader.LoadFieldPriorities();

        var fieldScores = new List<FieldScore>();

        var groups = context.Claims
            .GroupBy(c => c.ClaimKey, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();

            var claimsForField = group.ToList();
            if (claimsForField.Count == 0) continue;

            // ── Tier A: User locks — only honoured for user-contributed fields ──
            // Structured metadata (title, author, year, genre, etc.) is resolved
            // solely by the provider hierarchy. User locks are accepted only for
            // personal ratings, media-type corrections, and custom collection tags.
            if (UserLockableFields.Contains(group.Key))
            {
                var lockedClaims = claimsForField
                    .Where(c => c.IsUserLocked)
                    .ToList();

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
            }

            // ── Check for per-field provider priority override ───────────
            if (fieldPriorities.FieldOverrides.TryGetValue(group.Key, out var fieldOverride)
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
            // When multiple Wikidata claims exist for the same field (e.g. a
            // display-language title at 0.98 and a reconciliation match label at
            // 0.90), pick by highest confidence first, then newest as tiebreaker.
            var wikidataClaim = claimsForField
                .Where(c => c.ProviderId == WikidataProviderId)
                .OrderByDescending(c => c.Confidence)
                .ThenByDescending(c => c.ClaimedAt)
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
            // Pick the highest-confidence claim. When claims tie on confidence
            // (e.g. multiple dc:creator claims all at 1.0 from the EPUB processor),
            // prefer the EARLIEST claim so the first author in the file remains
            // the primary canonical author rather than the last one inserted.
            var bestClaim = claimsForField
                .OrderByDescending(c => c.Confidence)
                .ThenBy(c => c.ClaimedAt)
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
