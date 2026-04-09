using Microsoft.Extensions.Logging;
using MediaEngine.Domain;
using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Enums;
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

        // Refresh the FTS5 search index for this work. UpsertByEntityIdAsync
        // accepts either an asset id or a Work id and re-reads the canonical
        // state from the database — Self fields from the asset row, Parent
        // fields from the topmost Work row. Both persist passes
        // (asset and parent) call this; the second simply overwrites the row.
        if (searchIndex is not null)
        {
            await searchIndex.UpsertByEntityIdAsync(entityId, ct).ConfigureAwait(false);
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
    /// Lineage-aware persist + score (Phase 4 target architecture).
    ///
    /// Splits the inbound provider claims into two disjoint buckets using
    /// <see cref="ClaimScopeRegistry"/>:
    ///   • Self-scope claims  → written to the asset's own entity id.
    ///   • Parent-scope claims → written to <c>lineage.TargetForParentScope</c>,
    ///     which is the topmost Work in the hierarchy (the SHOW for TV, the
    ///     ALBUM for music, the SERIES for comics, the movie's own Work for
    ///     standalone movies).
    ///
    /// This split is unconditional and applies to every media type — including
    /// movies, where <c>TargetForParentScope == TargetForSelfScope</c>. In that
    /// case the parent claims still land on the movie's own Work id (not the
    /// media_assets row), giving every reader a single uniform lookup target:
    /// "parent-scoped fields live on the work; self-scoped fields live on the
    /// asset". No COALESCE, no fallback, no dual-write mirror.
    ///
    /// When <paramref name="lineage"/> is null, the entire write collapses to
    /// the asset id (legacy callers without a resolved hierarchy).
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
        // No lineage → fall back to single-target write on the asset id.
        if (lineage is null)
        {
            return await PersistClaimsAndScoreAsync(
                entityId, claims, providerId,
                claimRepo, canonicalRepo, scoringEngine, configLoader, allProviders, ct,
                arrayRepo, logger, searchIndex).ConfigureAwait(false);
        }

        // Partition claims by scope. The split is media-type aware.
        var selfClaims = new List<ProviderClaim>(claims.Count);
        var parentClaims = new List<ProviderClaim>(claims.Count);
        foreach (var c in claims)
        {
            if (string.IsNullOrWhiteSpace(c.Key))
                continue;

            if (ClaimScopeRegistry.GetScope(c.Key, lineage.MediaType) == ClaimScope.Parent)
                parentClaims.Add(c);
            else
                selfClaims.Add(c);
        }

        // 1. Asset-keyed write — only self-scope claims.
        var assetResult = await PersistClaimsAndScoreAsync(
            entityId, selfClaims, providerId,
            claimRepo, canonicalRepo, scoringEngine, configLoader, allProviders, ct,
            arrayRepo, logger, searchIndex).ConfigureAwait(false);

        // 2. Parent-Work write — only parent-scope claims, always against the
        //    topmost Work id (collapses to the movie's own Work for standalone
        //    media, so the data lives on works.id rather than media_assets.id).
        //
        //    Title synthesis for hierarchical parent Works: when the parent
        //    Work is a distinct entity from the asset (album, TV show, comic
        //    series, book/audiobook series, podcast show), the inbound claims
        //    never include a top-level "title" because title is Self-scoped
        //    (a track's title, an episode's title — not the container's).
        //    Without intervention, the parent Work has rich canonicals for
        //    album/show_name/series but no title, and the Registry surfaces
        //    it as "Untitled". Here we mint a synthetic title claim from the
        //    appropriate container field so every parent Work has a title.
        //
        //    Movies are skipped — they are standalone, title is Self-scoped,
        //    and the movie's own asset row already holds the title canonical.
        // Always attempt title synthesis — even when parentClaims is empty,
        // because for standalone media (Books, Audiobooks, Movies) the Work
        // row itself receives no claims at all (self-scope → asset, no
        // parent-scope claims), leaving the Work as "Untitled" in the Vault.
        MaybeSynthesizeParentTitle(claims, parentClaims, lineage, logger);

        if (parentClaims.Count > 0)
        {

            try
            {
                await PersistClaimsAndScoreAsync(
                    lineage.TargetForParentScope,
                    parentClaims,
                    providerId,
                    claimRepo, canonicalRepo, scoringEngine, configLoader, allProviders, ct,
                    arrayRepo, logger, searchIndex).ConfigureAwait(false);

                logger?.LogDebug(
                    "Lineage write: {ParentCount} parent-scope + {SelfCount} self-scope claim(s) for asset {AssetId} → parent Work {ParentWorkId} ({MediaType})",
                    parentClaims.Count, selfClaims.Count, entityId, lineage.TargetForParentScope, lineage.MediaType);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger?.LogWarning(ex,
                    "Parent-scope write failed for parent Work {ParentWorkId} (asset {AssetId}, {MediaType})",
                    lineage.TargetForParentScope, entityId, lineage.MediaType);
            }
        }

        return assetResult;
    }

    /// <summary>
    /// Mints a synthetic <c>title</c> claim for a hierarchical parent Work
    /// (album, TV show, comic series, book/audiobook series, podcast show)
    /// when the inbound parent-scope claims don't already carry one.
    ///
    /// Why this exists: <c>title</c> is Self-scoped by default — a track's
    /// title belongs to the track, not the album. As a result, parent Works
    /// accumulate rich canonicals (album, artist, year, cover_url, genre,
    /// apple IDs…) but no title, and the Registry surfaces them as
    /// "Untitled". Rather than teach every reader to fall back to the
    /// appropriate container field per media type, we write a canonical
    /// <c>title</c> on the parent Work at score-time, sourced from the
    /// appropriate container field:
    ///
    ///   • Music     → album
    ///   • TV        → show_name
    ///   • Podcasts  → show_name / podcast_name
    ///   • Comics    → series
    ///   • Books     → series
    ///   • Audiobooks → series
    ///
    /// Skipped when (a) the parent target collapses to the self target
    /// (standalone media — movies, single-volume books) because the asset's
    /// own title already lives on that row; (b) the parent claims already
    /// contain a title; or (c) no suitable container field is present.
    /// Confidence matches the source claim so Tier-D tie-breaks still work.
    /// </summary>
    private static void MaybeSynthesizeParentTitle(
        IReadOnlyList<ProviderClaim> allClaims,
        List<ProviderClaim> parentClaims,
        WorkLineage lineage,
        ILogger? logger)
    {
        // Already has a title claim in the parent batch — nothing to do.
        if (parentClaims.Any(c =>
                string.Equals(c.Key, MetadataFieldConstants.Title,
                    StringComparison.OrdinalIgnoreCase)))
            return;

        var isStandalone = lineage.TargetForParentScope == lineage.TargetForSelfScope;

        ProviderClaim? source = null;
        string? sourceKey = null;

        if (isStandalone)
        {
            // Standalone media (Books, Audiobooks, Movies, podcasts-no-show):
            // title is self-scoped and went to the asset row, not the Work
            // row. Copy it from the full claim list so the Work row renders
            // with a real title instead of "Untitled".
            source = allClaims
                .Where(c => string.Equals(c.Key, MetadataFieldConstants.Title,
                                StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(c.Value))
                .OrderByDescending(c => c.Confidence)
                .FirstOrDefault();
            sourceKey = MetadataFieldConstants.Title;
        }
        else
        {
            // Hierarchical parent (album, TV show, comic series, book
            // series, podcast show): mint a title from the container field.
            sourceKey = lineage.MediaType switch
            {
                MediaType.Music       => MetadataFieldConstants.Album,
                MediaType.TV          => MetadataFieldConstants.ShowName,
                MediaType.Podcasts    => MetadataFieldConstants.ShowName,
                MediaType.Comics      => MetadataFieldConstants.Series,
                MediaType.Books       => MetadataFieldConstants.Series,
                MediaType.Audiobooks  => MetadataFieldConstants.Series,
                _                     => null,
            };
            if (sourceKey is null)
                return;

            source = parentClaims
                .Where(c => string.Equals(c.Key, sourceKey, StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(c.Value))
                .OrderByDescending(c => c.Confidence)
                .FirstOrDefault();

            // Podcasts: also try podcast_name as a fallback.
            if (source is null && lineage.MediaType == MediaType.Podcasts)
            {
                source = parentClaims
                    .Where(c => string.Equals(c.Key, MetadataFieldConstants.PodcastName,
                                    StringComparison.OrdinalIgnoreCase)
                                && !string.IsNullOrWhiteSpace(c.Value))
                    .OrderByDescending(c => c.Confidence)
                    .FirstOrDefault();
            }
        }

        if (source is null)
            return;

        parentClaims.Add(new ProviderClaim(
            Key:        MetadataFieldConstants.Title,
            Value:      source.Value,
            Confidence: source.Confidence));

        logger?.LogDebug(
            "Synthesized parent-Work title '{Title}' from {SourceKey} for parent {ParentWorkId} ({MediaType}, standalone={Standalone})",
            source.Value, sourceKey, lineage.TargetForParentScope, lineage.MediaType, isStandalone);
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
