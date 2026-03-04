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
    public static async Task<ScoringResult> PersistClaimsAndScoreAsync(
        Guid entityId,
        IReadOnlyList<ProviderClaim> claims,
        Guid providerId,
        IMetadataClaimRepository claimRepo,
        ICanonicalValueRepository canonicalRepo,
        IScoringEngine scoringEngine,
        IConfigurationLoader configLoader,
        IEnumerable<IExternalMetadataProvider> allProviders,
        CancellationToken ct)
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
                EntityId     = entityId,
                Key          = f.Key,
                Value        = f.WinningValue!,
                LastScoredAt = scored.ScoredAt,
                IsConflicted = f.IsConflicted,
            })
            .ToList();

        await canonicalRepo.UpsertBatchAsync(canonicals, ct).ConfigureAwait(false);

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
