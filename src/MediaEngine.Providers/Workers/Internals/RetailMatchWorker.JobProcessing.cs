using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MediaEngine.Domain;
using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
using MediaEngine.Intelligence.Contracts;
using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Helpers;
using MediaEngine.Providers.Models;
using MediaEngine.Providers.Services;
using MediaEngine.Domain.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaEngine.Providers.Workers;

public sealed partial class RetailMatchWorker
{
    private async Task<Dictionary<string, string>> BuildFileHintsAsync(Guid entityId, CancellationToken ct)
    {
        var hints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var canonicals = await _canonicalRepo.GetByEntityAsync(entityId, ct);
        foreach (var c in canonicals)
        {
            if (!string.IsNullOrWhiteSpace(c.Key) && !string.IsNullOrWhiteSpace(c.Value))
                hints.TryAdd(c.Key, TextEncodingRepair.RepairMojibake(c.Value));
        }

        if (_arrayRepo is not null)
        {
            var arrays = await _arrayRepo.GetAllByEntityAsync(entityId, ct);
            foreach (var (key, entries) in arrays)
            {
                if (hints.ContainsKey(key))
                    continue;

                var values = entries
                    .OrderBy(entry => entry.Ordinal)
                    .Select(entry => TextEncodingRepair.RepairMojibake(entry.Value))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (values.Count > 0)
                    hints.TryAdd(key, JoinHintValues(key, values));
            }
        }

        var claims = await _claimRepo.GetByEntityAsync(entityId, ct);
        foreach (var group in claims
            .Where(claim => !string.IsNullOrWhiteSpace(claim.ClaimKey)
                && !string.IsNullOrWhiteSpace(claim.ClaimValue))
            .GroupBy(claim => claim.ClaimKey, StringComparer.OrdinalIgnoreCase))
        {
            if (hints.ContainsKey(group.Key))
                continue;

            var values = group
                .OrderByDescending(claim => claim.IsUserLocked)
                .ThenByDescending(claim => claim.Confidence)
                .ThenByDescending(claim => claim.ClaimedAt)
                .Select(claim => TextEncodingRepair.RepairMojibake(claim.ClaimValue.Trim()))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (values.Count > 0)
                hints.TryAdd(group.Key, JoinHintValues(group.Key, values));
        }

        return hints;
    }

    private static string JoinHintValues(string key, IReadOnlyList<string> values)
    {
        if (values.Count == 1 || !IsMultiValueCreatorHint(key))
            return values[0];

        return string.Join(" and ", values);
    }

    private static bool IsMultiValueCreatorHint(string key) =>
        key.Equals(MetadataFieldConstants.Author, StringComparison.OrdinalIgnoreCase)
        || key.Equals(MetadataFieldConstants.Artist, StringComparison.OrdinalIgnoreCase)
        || key.Equals(MetadataFieldConstants.Composer, StringComparison.OrdinalIgnoreCase)
        || key.Equals(MetadataFieldConstants.Director, StringComparison.OrdinalIgnoreCase)
        || key.Equals(MetadataFieldConstants.Narrator, StringComparison.OrdinalIgnoreCase)
        || key.Equals(MetadataFieldConstants.Illustrator, StringComparison.OrdinalIgnoreCase)
        || key.Equals("writer", StringComparison.OrdinalIgnoreCase);

    private async Task<bool> TryRouteMusicLocalIdentityFallbackAsync(
        IdentityJob job,
        IReadOnlyDictionary<string, string> hints,
        double bestRetailScore,
        CancellationToken ct)
    {
        if (!Enum.TryParse<MediaType>(job.MediaType, true, out var mediaType)
            || mediaType != MediaType.Music)
        {
            return false;
        }

        var title = hints.GetValueOrDefault(MetadataFieldConstants.Title);
        var artist = GetMusicCreatorHint(hints);

        if (PlaceholderTitleDetector.IsPlaceholder(title)
            || string.IsNullOrWhiteSpace(artist))
        {
            return false;
        }

        await _jobRepo.UpdateStateAsync(
            job.Id,
            IdentityJobState.RetailMatchedNeedsReview,
            "Retail did not accept a music match; attempting Wikidata from local title and artist.",
            ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Music identity fallback queued for entity {EntityId}: '{Title}' by '{Artist}' (best retail score {Score:F2})",
            job.EntityId,
            title,
            artist,
            bestRetailScore);

        return true;
    }

    internal async Task ProcessJobAsync(IdentityJob job, CancellationToken ct)
    {
        await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.RetailSearching, ct: ct);

        if (!Enum.TryParse<MediaType>(job.MediaType, true, out var mediaType))
            mediaType = MediaType.Unknown;

        // Look up the asset's work lineage once. Used by the router below to
        // write provider bridge IDs to the correct Work — track-level IDs
        // (apple_music_id, isrc) on the asset's own Work; album-level IDs
        // (apple_music_collection_id, musicbrainz_id) on the parent. Null
        // when the job targets a Work directly (manual flows) — in that case
        // we skip work-level routing entirely.
        WorkLineage? lineage = null;
        if (string.Equals(job.EntityType, "MediaAsset", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                lineage = await _workRepo.GetLineageByAssetAsync(job.EntityId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Lineage lookup failed for asset {EntityId} — work-level external_identifiers writes will be skipped",
                    job.EntityId);
            }
        }

        // Load pipeline configuration for this media type
        var pipelineConfig = _configLoader.LoadPipelines();
        var pipeline = pipelineConfig.GetPipelineForMediaType(job.MediaType);
        var strategy = pipeline.Strategy;
        var hydrationConfig = _configLoader.LoadHydration();

        var retailAcceptThreshold = hydrationConfig.RetailAutoAcceptThreshold;
        var retailAmbiguousThreshold = hydrationConfig.RetailAmbiguousThreshold;

        // Get ranked providers for this media type
        var providerConfigs = _configLoader.LoadAllProviders();
        var rankedProviders = pipeline.Providers.Count > 0
            ? pipeline.Providers.OrderBy(p => p.Rank).Select(p => p.Name).ToList()
            : providerConfigs.Select(p => p.Name).ToList();
        var enabledProviders = ProviderExecutionFilter.EnabledProviderNames(
            rankedProviders,
            _providers,
            providerConfigs);
        if (enabledProviders.Count == 0)
        {
            var message = $"No enabled retail provider is configured for media type '{job.MediaType}'.";
            await _jobRepo.ScheduleRetryAsync(
                job.Id,
                IdentityJobState.Queued,
                DateTimeOffset.UtcNow.AddMinutes(30),
                message,
                ct).ConfigureAwait(false);

            _logger.LogWarning(
                "RetailMatchWorker: {Message} Entity {EntityId} will stay queued instead of becoming no-match.",
                message,
                job.EntityId);
            return;
        }

        // Build hints from existing canonicals plus claim fallbacks. Some local
        // processor evidence, especially authors, can be multi-valued and may
        // not have a scalar canonical yet.
        var hints = await BuildFileHintsAsync(job.EntityId, ct);
        var authorHint = hints.GetValueOrDefault(MetadataFieldConstants.Author);
        var artistHint = hints.GetValueOrDefault(MetadataFieldConstants.Artist);
        var composerHint = hints.GetValueOrDefault(MetadataFieldConstants.Composer);
        if (mediaType == MediaType.Music)
        {
            var musicCreatorHint = GetMusicCreatorHint(hints);
            artistHint = musicCreatorHint;
            authorHint = StringHelpers.FirstNonBlank(authorHint, musicCreatorHint);
        }

        var allCandidates = new List<RetailMatchCandidate>();
        RetailMatchCandidate? bestCandidate = null;
        var bestScore = 0.0;
        var providerRank = 0;
        var providerFailures = 0;
        var sequentialBridgeIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var acceptedIdentity = false;
        var acceptedEnrichmentProviders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Iterate providers per strategy
        foreach (var providerName in enabledProviders)
        {
            providerRank++;
            var pipelineEntry = pipeline.Providers.FirstOrDefault(entry =>
                string.Equals(entry.Name, providerName, StringComparison.OrdinalIgnoreCase));
            if (pipelineEntry?.RequiresIdentity == true && !acceptedIdentity)
            {
                _logger.LogInformation(
                    "Provider {Provider} skipped for entity {EntityId} because its configured enrichment role requires an accepted identity",
                    providerName,
                    job.EntityId);
                continue;
            }

            var provider = _providers.FirstOrDefault(p =>
                string.Equals(p.Name, providerName, StringComparison.OrdinalIgnoreCase));

            if (provider is null) continue;

            try
            {
                // Build lookup request
                var lookupRequest = new ProviderLookupRequest
                {
                    EntityId = job.EntityId,
                    EntityType = EntityType.MediaAsset,
                    MediaType = mediaType,
                    Title = hints.GetValueOrDefault(MetadataFieldConstants.Title),
                    Author = authorHint,
                    Year = hints.GetValueOrDefault(MetadataFieldConstants.Year),
                    Narrator = hints.GetValueOrDefault(MetadataFieldConstants.Narrator),
                    ShowName = hints.GetValueOrDefault(MetadataFieldConstants.ShowName)
                        ?? hints.GetValueOrDefault(MetadataFieldConstants.Series),
                    Album = hints.GetValueOrDefault(MetadataFieldConstants.Album),
                    Artist = artistHint,
                    Composer = composerHint,
                    Director = hints.GetValueOrDefault(MetadataFieldConstants.Director),
                    SeasonNumber = hints.GetValueOrDefault(MetadataFieldConstants.SeasonNumber)
                        ?? hints.GetValueOrDefault("season"),
                    EpisodeNumber = hints.GetValueOrDefault(MetadataFieldConstants.EpisodeNumber)
                        ?? hints.GetValueOrDefault("episode"),
                    TrackNumber = hints.GetValueOrDefault(MetadataFieldConstants.TrackNumber),
                    Series = hints.GetValueOrDefault(MetadataFieldConstants.Series),
                    Genre = hints.GetValueOrDefault(MetadataFieldConstants.Genre),
                    Isbn = hints.GetValueOrDefault(BridgeIdKeys.Isbn),
                    Asin = hints.GetValueOrDefault(BridgeIdKeys.Asin),
                    Hints = hints,
                    PriorProviderBridgeIds = strategy == ProviderStrategy.Sequential
                        ? sequentialBridgeIds : null,
                };

                var claims = await provider.FetchAsync(lookupRequest, ct);
                if (claims.Count == 0) continue;

                // Extract candidate metadata from claims
                var candidateTitle = claims
                    .FirstOrDefault(c => string.Equals(c.Key, MetadataFieldConstants.Title,
                        StringComparison.OrdinalIgnoreCase))?.Value;
                var candidateAuthor = claims
                    .FirstOrDefault(c => string.Equals(c.Key, MetadataFieldConstants.Author,
                        StringComparison.OrdinalIgnoreCase))?.Value;
                var candidateYear = claims
                    .FirstOrDefault(c => string.Equals(c.Key, MetadataFieldConstants.Year,
                        StringComparison.OrdinalIgnoreCase))?.Value;

                var (structuralBonus, structuralEvidence) = ComputeSingleItemStructuralSignal(
                    mediaType, hints, claims);
                var extendedMetadata = BuildCandidateExtendedMetadata(claims);

                // Score candidate
                var retailScore = _retailScoring.ScoreCandidate(
                    hints, candidateTitle, candidateAuthor, candidateYear, mediaType,
                    extendedMetadata: extendedMetadata,
                    structuralBonus: structuralBonus);

                var decision = _candidateScorer.EvaluateDecision(
                    hints,
                    candidateTitle,
                    candidateAuthor,
                    candidateYear,
                    retailScore,
                    retailScore.CompositeScore,
                    retailAcceptThreshold,
                    retailAmbiguousThreshold,
                    "single_item",
                    mediaType: mediaType,
                    extendedMetadata: extendedMetadata);

                // Extract bridge IDs from claims
                var bridgeIdsJson = BuildBridgeIdsJson(claims);

                // Build candidate record
                var candidate = new RetailMatchCandidate
                {
                    JobId = job.Id,
                    ProviderId = provider.ProviderId,
                    ProviderName = provider.Name,
                    ProviderItemId = claims
                        .FirstOrDefault(c => string.Equals(c.Key, "provider_item_id",
                            StringComparison.OrdinalIgnoreCase))?.Value,
                    Rank = providerRank,
                    Title = candidateTitle ?? "(unknown)",
                    Creator = candidateAuthor,
                    Year = candidateYear,
                    ScoreTotal = decision.FinalScore,
                    ScoreBreakdownJson = _candidateScorer.BuildScoreBreakdownJson(
                        retailScore,
                        decision,
                        "single_item",
                        structuralEvidence,
                        structuralBonus),
                    BridgeIdsJson = bridgeIdsJson,
                    Description = claims
                        .FirstOrDefault(c => string.Equals(c.Key, MetadataFieldConstants.Description,
                            StringComparison.OrdinalIgnoreCase))?.Value,
                    ImageUrl = claims
                        .FirstOrDefault(c => string.Equals(c.Key, MetadataFieldConstants.CoverUrl,
                            StringComparison.OrdinalIgnoreCase))?.Value,
                    Outcome = decision.Outcome,
                };

                allCandidates.Add(candidate);

                if (IsIdentityPurpose(pipelineEntry?.Purpose)
                    && decision.Outcome == "AutoAccepted")
                {
                    acceptedIdentity = true;
                }

                // Track best candidate
                if (IsBetterCandidate(candidate, bestCandidate))
                {
                    bestScore = candidate.ScoreTotal;
                    bestCandidate = candidate;
                }

                var shouldPersistProviderClaims = ShouldPersistProviderClaims(
                    decision,
                    pipelineEntry,
                    acceptedIdentity,
                    retailScore,
                    claims);

                // Persist claims if candidate is accepted/ambiguous, or if a configured
                // enrichment provider safely corroborates the accepted identity.
                if (shouldPersistProviderClaims)
                {
                    if (string.Equals(pipelineEntry?.Purpose, "enrichment", StringComparison.OrdinalIgnoreCase))
                        acceptedEnrichmentProviders.Add(provider.Name);

                    // Phase 3c: pass lineage so parent-scope claims mirror
                    // onto the parent Work (book series → series Work,
                    // audiobook series → series Work, etc.).
                    await ScoringHelper.PersistAndScoreWithLineageAsync(
                        job.EntityId, claims, provider.ProviderId, lineage,
                        _claimRepo, _canonicalRepo, _scoringEngine, _configLoader, _providers, ct,
                        arrayRepo: _arrayRepo, logger: _logger);

                    // Extract bridge IDs for Stage 2
                    var bridgeEntries = claims
                        .Where(c => BridgeIdHelper.IsBridgeId(c.Key) && !string.IsNullOrWhiteSpace(c.Value))
                        .Select(c => new BridgeIdEntry
                        {
                            EntityId = ResolveBridgeIdEntityId(lineage, job.EntityId, c.Key),
                            IdType = c.Key,
                            IdValue = c.Value,
                            ProviderId = provider.ProviderId.ToString(),
                        }).ToList();

                    if (bridgeEntries.Count > 0)
                        await _bridgeIdRepo.UpsertBatchAsync(bridgeEntries, ct);

                    // Phase 3b: also write provider bridge IDs to the appropriate
                    // Work's external_identifiers JSON. Track-level IDs land on
                    // the asset's own Work; album/show/series-level IDs land on
                    // the parent. WriteExternalIdentifiersAsync is no-overwrite,
                    // so re-running this for sibling tracks of the same album
                    // is harmless.
                    if (lineage is not null && bridgeEntries.Count > 0)
                    {
                        var bridgeDict = bridgeEntries
                            .GroupBy(b => b.IdType, StringComparer.OrdinalIgnoreCase)
                            .ToDictionary(g => g.Key, g => g.First().IdValue,
                                StringComparer.OrdinalIgnoreCase);

                        var (forParent, forSelf) = _claimRouter.SplitBridgeIds(lineage, bridgeDict);

                        if (forParent.Count > 0)
                        {
                            await _workRepo.WriteExternalIdentifiersAsync(
                                lineage.TargetForParentScope, forParent, ct);
                        }

                        if (forSelf.Count > 0)
                        {
                            await _workRepo.WriteExternalIdentifiersAsync(
                                lineage.TargetForSelfScope, forSelf, ct);
                        }
                    }

                    // Sequential: accumulate bridge IDs for next provider
                    if (strategy == ProviderStrategy.Sequential)
                    {
                        foreach (var c in claims.Where(c => BridgeIdHelper.IsBridgeId(c.Key)))
                            sequentialBridgeIds.TryAdd(c.Key, c.Value);
                    }
                }

                // Waterfall: stop after first accepted candidate
                if (strategy == ProviderStrategy.Waterfall && decision.Outcome == "AutoAccepted")
                    break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                providerFailures++;
                _logger.LogWarning(ex,
                    "Provider {Provider} failed for entity {EntityId}",
                    providerName, job.EntityId);
            }
        }

        if (mediaType == MediaType.Music
            && lineage is not null
            && acceptedEnrichmentProviders.Contains("apple_api"))
        {
            await PersistAcceptedAppleAlbumManifestAsync(lineage, hints, sequentialBridgeIds, ct)
                .ConfigureAwait(false);
        }

        // Persist ALL candidates (winners and losers)
        if (allCandidates.Count > 0)
            await _candidateRepo.InsertBatchAsync(allCandidates, ct);

        bestCandidate = SelectIdentityCandidateWhenConfigured(allCandidates, bestCandidate, pipeline);
        bestScore = bestCandidate?.ScoreTotal ?? 0.0;

        // Determine final job state based on best candidate
        if (bestCandidate is not null && bestCandidate.Outcome == "AutoAccepted")
        {
            await _jobRepo.SetSelectedCandidateAsync(job.Id, bestCandidate.Id, ct);
            await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.RetailMatched, ct: ct);
            await PersistProviderProvenanceAsync(
                job.EntityId,
                bestCandidate.ProviderName,
                acceptedEnrichmentProviders,
                ct).ConfigureAwait(false);
            await _timeline.RecordRetailMatchedAsync(
                job.EntityId, bestCandidate.ProviderName,
                allCandidates.Count, job.IngestionRunId, ct);

            _logger.LogInformation(
                "Retail match found for entity {EntityId}: '{Title}' from {Provider} (score: {Score:F2})",
                job.EntityId, bestCandidate.Title, bestCandidate.ProviderName, bestScore);

            try
            {
                await _postPipeline.EvaluateAndOrganizeAsync(
                    job.EntityId, job.Id, wikidataQid: null, job.IngestionRunId, ct)
                    .ConfigureAwait(false);
                if (_coverArtWorker is not null)
                    await _coverArtWorker.DownloadAndPersistAsync(job.EntityId, wikidataQid: null, ct)
                        .ConfigureAwait(false);
            }
            catch (Exception orgEx) when (orgEx is not OperationCanceledException)
            {
                _logger.LogWarning(orgEx,
                    "Post-retail organization failed for entity {EntityId} — pipeline continues",
                    job.EntityId);
            }
        }
        else if (bestCandidate is not null && bestCandidate.Outcome == "Ambiguous")
        {
            await _jobRepo.SetSelectedCandidateAsync(job.Id, bestCandidate.Id, ct);
            await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.RetailMatchedNeedsReview, ct: ct);
            await _outcomeFactory.CreateRetailAmbiguousAsync(
                job.EntityId, job.MediaType, bestScore, job.IngestionRunId, null, ct);
            await _timeline.RecordRetailMatchedAsync(
                job.EntityId, bestCandidate.ProviderName,
                allCandidates.Count, job.IngestionRunId, ct);

            _logger.LogInformation(
                "Retail match ambiguous for entity {EntityId}: '{Title}' (score: {Score:F2})",
                job.EntityId, bestCandidate.Title, bestScore);
        }
        else if (providerFailures > 0)
        {
            var message = $"Retail provider lookup failed for {providerFailures} provider(s); retrying before no-match classification.";
            await _jobRepo.ScheduleRetryAsync(
                job.Id,
                IdentityJobState.Queued,
                DateTimeOffset.UtcNow.AddMinutes(10),
                message,
                ct).ConfigureAwait(false);

            _logger.LogWarning(
                "RetailMatchWorker: {Message} Entity {EntityId}; candidates evaluated: {CandidateCount}, best score: {Score:F2}",
                message,
                job.EntityId,
                allCandidates.Count,
                bestScore);
        }
        else
        {
            if (await TryRouteMusicLocalIdentityFallbackAsync(job, hints, bestScore, ct)
                    .ConfigureAwait(false))
            {
                return;
            }

            await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.RetailNoMatch, ct: ct);

            var titleHint = hints.GetValueOrDefault(MetadataFieldConstants.Title);

            // If the file has a placeholder title with no bridge IDs, route to a
            // dedicated PlaceholderTitle review trigger instead of the generic
            // RetailMatchFailed bucket — these items will never match retail.
            if (PlaceholderTitleDetector.IsPlaceholder(titleHint)
                && !PlaceholderTitleDetector.HasBridgeId(hints))
            {
                await _outcomeFactory.CreatePlaceholderTitleAsync(
                    job.EntityId, titleHint, job.IngestionRunId, null, ct);
            }
            else
            {
                await _outcomeFactory.CreateRetailFailedAsync(
                    job.EntityId, job.MediaType, job.IngestionRunId, null, ct);
            }

            await _timeline.RecordRetailNoMatchAsync(
                job.EntityId, titleHint ?? "(unknown)", job.IngestionRunId, ct);

            _logger.LogInformation(
                "No retail match for entity {EntityId} — {CandidateCount} candidates evaluated, best score: {Score:F2}",
                job.EntityId, allCandidates.Count, bestScore);
        }
    }

    private static Guid ResolveBridgeIdEntityId(WorkLineage? lineage, Guid assetId, string key)
    {
        if (lineage is null)
            return assetId;

        return ClaimScopeCatalog.IsParentScoped(key, lineage.MediaType)
            ? lineage.TargetForParentScope
            : lineage.TargetForSelfScope;
    }

    private async Task PersistProviderProvenanceAsync(
        Guid entityId,
        string identityProvider,
        IReadOnlyCollection<string> enrichmentProviders,
        CancellationToken ct)
    {
        await _canonicalRepo.UpsertBatchAsync(
        [
            new CanonicalValue
            {
                EntityId = entityId,
                Key = MetadataFieldConstants.IdentityProvider,
                Value = identityProvider,
                LastScoredAt = DateTimeOffset.UtcNow,
            },
        ], ct).ConfigureAwait(false);

        if (_arrayRepo is null)
            return;

        var entries = enrichmentProviders
            .OrderBy(provider => provider, StringComparer.OrdinalIgnoreCase)
            .Select((provider, ordinal) => new CanonicalArrayEntry
            {
                Ordinal = ordinal,
                Value = provider,
            })
            .ToList();
        await _arrayRepo.SetValuesAsync(
            entityId,
            MetadataFieldConstants.EnrichmentProviders,
            entries,
            ct).ConfigureAwait(false);
    }

}
