using Microsoft.Extensions.Logging;
using MediaEngine.Domain;
using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Intelligence.Contracts;
using MediaEngine.Intelligence.Models;
using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Models;
using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Models;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Shared helper that encapsulates the claim-persist-score-upsert pattern
/// used by both <see cref="MetadataHarvestingService"/> and
/// <see cref="HydrationPipelineService"/>.
///
/// This avoids duplicating the same 40-line scoring block across multiple services.
/// </summary>
public static class ScoringHelper
{
    /// <summary>
    /// Multi-valued field keys — delegates to
    /// <see cref="MetadataFieldConstants.MultiValuedKeys"/>.
    /// </summary>
    public static HashSet<string> MultiValuedKeys => MetadataFieldConstants.MultiValuedKeys;

    /// <summary>
    /// Persists new claims for an entity, loads all claims, runs the scoring
    /// engine, and upserts the resulting canonical values.
    /// </summary>
    /// <param name="entityId">The entity being scored.</param>
    /// <param name="claims">New provider claims to persist (may be empty).</param>
    /// <param name="providerId">The provider GUID that produced these claims.</param>
    /// <param name="claimRepo">Claims repository.</param>
    /// <param name="canonicalRepo">Canonical values repository.</param>
    /// <param name="scoringEngine">The intelligence scoring engine.</param>
    /// <param name="configLoader">Configuration loader for scoring settings and provider weights.</param>
    /// <param name="allProviders">All registered providers (for weight map building).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The scoring result after re-scoring the entity.</returns>
    public static async Task<ScoringResult> PersistClaimsAndScoreAsync(
        Guid entityId,
        IReadOnlyList<ProviderClaim> claims,
        Guid providerId,
        IMetadataClaimRepository claimRepo,
        ICanonicalValueRepository canonicalRepo,
        IScoringEngine scoringEngine,
        IConfigurationLoader configLoader,
        IEnumerable<IExternalMetadataProvider> allProviders,
        CancellationToken ct,
        ICanonicalValueArrayRepository? arrayRepo = null,
        ILogger? logger = null,
        ISearchIndexRepository? searchIndex = null)
    {
        // Wrap provider claims as domain MetadataClaim rows.
        var domainClaims = claims
            .Select(pc => new MetadataClaim
            {
                Id           = Guid.NewGuid(),
                EntityId     = entityId,
                ProviderId   = providerId,
                ClaimKey     = pc.Key,
                ClaimValue   = pc.Value,
                Confidence   = pc.Confidence,
                ClaimedAt    = DateTimeOffset.UtcNow,
                IsUserLocked = false,
            })
            .ToList();

        // Persist claims (append-only).
        if (domainClaims.Count > 0)
        {
            await claimRepo.InsertBatchAsync(domainClaims, ct).ConfigureAwait(false);
        }

        // Load ALL claims for this entity and re-score.
        var allClaims       = await claimRepo.GetByEntityAsync(entityId, ct).ConfigureAwait(false);
        var scoringSettings = configLoader.LoadScoring();
        var providerConfigs = configLoader.LoadAllProviders();

        var (providerWeights, providerFieldWeights) = BuildWeightMaps(providerConfigs, allProviders);

        var scoringConfig = new ScoringConfiguration
        {
            AutoLinkThreshold     = scoringSettings.AutoLinkThreshold,
            ConflictThreshold     = scoringSettings.ConflictThreshold,
            ConflictEpsilon       = scoringSettings.ConflictEpsilon,
            StaleClaimDecayDays   = scoringSettings.StaleClaimDecayDays,
            StaleClaimDecayFactor = scoringSettings.StaleClaimDecayFactor,
        };

        var scoringContext = new ScoringContext
        {
            EntityId             = entityId,
            Claims               = allClaims,
            ProviderWeights      = providerWeights,
            ProviderFieldWeights = providerFieldWeights,
            Configuration        = scoringConfig,
        };

        var scored = await scoringEngine.ScoreEntityAsync(scoringContext, ct).ConfigureAwait(false);

        // Upsert canonical values (current best answers).
        var canonicals = scored.FieldScores
            .Where(f => !string.IsNullOrEmpty(f.WinningValue))
            .Select(f => new CanonicalValue
            {
                EntityId          = entityId,
                Key               = f.Key,
                Value             = f.WinningValue!,
                LastScoredAt      = scored.ScoredAt,
                IsConflicted      = f.IsConflicted,
                WinningProviderId = f.WinningProviderId,
                NeedsReview       = f.IsConflicted, // Unit 5: conflicted fields immediately flagged for review
            })
            .ToList();

        // Sanitise single-valued fields: strip leaked ||| separators.
        foreach (var cv in canonicals)
        {
            if (!MultiValuedKeys.Contains(cv.Key)
                && cv.Value.Contains("|||", StringComparison.Ordinal))
            {
                var first = cv.Value.Split("|||",
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .FirstOrDefault();
                if (!string.IsNullOrEmpty(first))
                    cv.Value = first;
            }
        }

        await canonicalRepo.UpsertBatchAsync(canonicals, ct).ConfigureAwait(false);

        // Update FTS5 search index when any searchable field changes.
        // Phase 2C: index title, original_title, alternate_titles, author, description
        // for cross-language search (e.g. "Amélie" matches via unicode61 tokenizer).
        if (searchIndex is not null)
        {
            var titleVal         = canonicals.FirstOrDefault(c => c.Key == "title")?.Value;
            var originalTitleVal = canonicals.FirstOrDefault(c => c.Key == "original_title")?.Value;
            var authorVal        = canonicals.FirstOrDefault(c => c.Key == "author")?.Value;
            var descriptionVal   = canonicals.FirstOrDefault(c => c.Key == "description")?.Value;
            // alternate_titles are stored in canonical_value_arrays; UpsertByEntityIdAsync
            // resolves them from the database via the work_id join.
            if (titleVal is not null || originalTitleVal is not null || authorVal is not null)
            {
                await searchIndex.UpsertByEntityIdAsync(
                    entityId,
                    titleVal,
                    originalTitleVal,
                    alternateTitles: null,  // populated by RebuildAsync from canonical_value_arrays
                    authorVal,
                    descriptionVal,
                    ct).ConfigureAwait(false);
            }
        }

        // Decompose multi-valued fields into proper array rows.
        // Instead of splitting a single winning value on |||, collect ALL claims
        // from the winning provider for each multi-valued key.
        if (arrayRepo is not null)
        {
            foreach (var fieldScore in scored.FieldScores)
            {
                if (!MetadataFieldConstants.IsMultiValued(fieldScore.Key))
                    continue;

                // Skip companion _qid keys — they are paired with their parent key below.
                if (fieldScore.Key.EndsWith(MetadataFieldConstants.CompanionQidSuffix,
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    // Collect ALL claims from the winning provider for this key.
                    // Deduplicate by value (case-insensitive) to prevent "Author & Author" display.
                    var winningClaims = allClaims
                        .Where(c => c.ProviderId == fieldScore.WinningProviderId
                                 && c.ClaimKey.Equals(fieldScore.Key, StringComparison.OrdinalIgnoreCase))
                        .DistinctBy(c => c.ClaimValue, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (winningClaims.Count == 0)
                        continue;

                    // Collect companion _qid claims from the same provider.
                    var qidKey = fieldScore.Key + MetadataFieldConstants.CompanionQidSuffix;
                    var qidClaims = allClaims
                        .Where(c => c.ProviderId == fieldScore.WinningProviderId
                                 && c.ClaimKey.Equals(qidKey, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    var entries = new List<CanonicalArrayEntry>(winningClaims.Count);
                    for (int i = 0; i < winningClaims.Count; i++)
                    {
                        entries.Add(new CanonicalArrayEntry
                        {
                            Ordinal  = i,
                            Value    = winningClaims[i].ClaimValue,
                            ValueQid = i < qidClaims.Count ? qidClaims[i].ClaimValue : null,
                        });
                    }

                    await arrayRepo.SetValuesAsync(entityId, fieldScore.Key, entries, ct)
                        .ConfigureAwait(false);

                    // Update canonical_values to store the joined string of all values
                    // so that display fields (e.g. author) show all values (e.g. "Neil Gaiman; Terry Pratchett").
                    var canonical = canonicals.FirstOrDefault(c =>
                        c.Key.Equals(fieldScore.Key, StringComparison.OrdinalIgnoreCase));
                    if (canonical is not null && winningClaims.Count > 1)
                        canonical.Value = string.Join("; ", winningClaims.Select(c => c.ClaimValue));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger?.LogDebug(ex,
                        "Failed to decompose multi-valued field '{Key}' for entity {EntityId}",
                        fieldScore.Key, entityId);
                }
            }
        }

        return scored;
    }

    /// <summary>
    /// Phase 3c: lineage-aware persist + score. Runs the existing per-asset
    /// persistence path unchanged (so no Vault reader regresses), and then —
    /// when the asset has a real parent in the Work hierarchy — mirrors the
    /// claims tagged with <see cref="ClaimScope.Parent"/> onto the parent
    /// Work id as a second persist+score pass.
    ///
    /// The result is "dual write": the asset row keeps its full canonical
    /// values picture for backward compatibility, and the parent Work gains
    /// an authoritative copy of the parent-scoped fields (show_name, album,
    /// year for music, etc.). Phase 4 readers will consult the parent Work,
    /// and Phase 5 will retire the asset-side mirror once the registry CTEs
    /// are ported.
    ///
    /// When <paramref name="lineage"/> is null, or when the asset is its own
    /// root (movies, single-volume books), the parent pass is a no-op and
    /// behaviour is identical to <see cref="PersistClaimsAndScoreAsync"/>.
    /// The parent-side mirror is best-effort: failures are logged but never
    /// break the asset-side write.
    /// </summary>
    public static async Task<ScoringResult> PersistAndScoreWithLineageAsync(
        Guid entityId,
        IReadOnlyList<ProviderClaim> claims,
        Guid providerId,
        WorkLineage? lineage,
        IMetadataClaimRepository claimRepo,
        ICanonicalValueRepository canonicalRepo,
        IScoringEngine scoringEngine,
        IConfigurationLoader configLoader,
        IEnumerable<IExternalMetadataProvider> allProviders,
        CancellationToken ct,
        ICanonicalValueArrayRepository? arrayRepo = null,
        ILogger? logger = null,
        ISearchIndexRepository? searchIndex = null)
    {
        // 1. Asset-keyed write — unchanged behaviour, preserves all current
        //    Vault reads.
        var assetResult = await PersistClaimsAndScoreAsync(
            entityId, claims, providerId,
            claimRepo, canonicalRepo, scoringEngine, configLoader, allProviders, ct,
            arrayRepo, logger, searchIndex).ConfigureAwait(false);

        // 2. Parent-Work mirror — only when the asset has a real parent.
        if (lineage is null
            || lineage.TargetForParentScope == lineage.TargetForSelfScope
            || claims.Count == 0)
        {
            return assetResult;
        }

        var parentClaims = claims
            .Where(c => !string.IsNullOrWhiteSpace(c.Key)
                     && ClaimScopeRegistry.GetScope(c.Key, lineage.MediaType) == ClaimScope.Parent)
            .ToList();

        if (parentClaims.Count == 0)
            return assetResult;

        try
        {
            await PersistClaimsAndScoreAsync(
                lineage.TargetForParentScope,
                parentClaims,
                providerId,
                claimRepo, canonicalRepo, scoringEngine, configLoader, allProviders, ct,
                arrayRepo, logger, searchIndex).ConfigureAwait(false);

            logger?.LogDebug(
                "Phase 3c: mirrored {Count} parent-scope claim(s) for asset {AssetId} → parent Work {ParentWorkId} ({MediaType})",
                parentClaims.Count, entityId, lineage.TargetForParentScope, lineage.MediaType);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex,
                "Phase 3c parent-scope mirror write failed for parent Work {ParentWorkId} (asset {AssetId})",
                lineage.TargetForParentScope, entityId);
        }

        return assetResult;
    }

    /// <summary>
    /// Builds provider weight maps from provider configs and registered providers.
    /// </summary>
    public static (IReadOnlyDictionary<Guid, double> Weights,
                     IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, double>>? FieldWeights)
        BuildWeightMaps(
            IReadOnlyList<Storage.Models.ProviderConfiguration> providerConfigs,
            IEnumerable<IExternalMetadataProvider> providers)
    {
        var weights      = new Dictionary<Guid, double>();
        Dictionary<Guid, IReadOnlyDictionary<string, double>>? fieldWeights = null;

        foreach (var provider in providers)
        {
            var provConfig = providerConfigs
                .FirstOrDefault(p => string.Equals(p.Name, provider.Name,
                    StringComparison.OrdinalIgnoreCase));

            if (provConfig is null) continue;

            weights[provider.ProviderId] = provConfig.Weight;

            if (provConfig.FieldWeights.Count > 0)
            {
                fieldWeights ??= new Dictionary<Guid, IReadOnlyDictionary<string, double>>();
                fieldWeights[provider.ProviderId] =
                    (IReadOnlyDictionary<string, double>)provConfig.FieldWeights;
            }
        }

        return (weights, fieldWeights);
    }
}
