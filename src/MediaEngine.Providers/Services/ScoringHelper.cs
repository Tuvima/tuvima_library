using Microsoft.Extensions.Logging;
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
internal static class ScoringHelper
{
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
    /// <summary>
    /// Multi-valued field keys that should be decomposed into
    /// <see cref="ICanonicalValueArrayRepository"/> rows when their winning
    /// canonical value contains the <c>|||</c> separator.
    /// </summary>
    private static readonly HashSet<string> MultiValuedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "genre", "characters", "cast_member", "voice_actor",
        "narrative_location", "main_subject", "composer", "screenwriter",
    };

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
        ILogger? logger = null)
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

        await canonicalRepo.UpsertBatchAsync(canonicals, ct).ConfigureAwait(false);

        // Decompose multi-valued winning values (containing |||) into proper array rows.
        if (arrayRepo is not null)
        {
            foreach (var canonical in canonicals)
            {
                if (!MultiValuedKeys.Contains(canonical.Key))
                    continue;

                if (!canonical.Value.Contains("|||", StringComparison.Ordinal))
                    continue;

                try
                {
                    var parts = canonical.Value.Split("|||",
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                    // Look for a matching _qid canonical value for QID pairing.
                    var qidCanonical = canonicals
                        .FirstOrDefault(c => string.Equals(c.Key, canonical.Key + "_qid",
                            StringComparison.OrdinalIgnoreCase));
                    var qids = qidCanonical?.Value.Split("|||",
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                    var entries = new List<CanonicalArrayEntry>(parts.Length);
                    for (int i = 0; i < parts.Length; i++)
                    {
                        entries.Add(new CanonicalArrayEntry
                        {
                            Ordinal  = i,
                            Value    = parts[i],
                            ValueQid = qids is not null && i < qids.Length ? qids[i] : null,
                        });
                    }

                    await arrayRepo.SetValuesAsync(entityId, canonical.Key, entries, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger?.LogDebug(ex,
                        "Failed to decompose multi-valued field '{Key}' for entity {EntityId}",
                        canonical.Key, entityId);
                }
            }
        }

        return scored;
    }

    /// <summary>
    /// Builds provider weight maps from provider configs and registered providers.
    /// </summary>
    internal static (IReadOnlyDictionary<Guid, double> Weights,
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
