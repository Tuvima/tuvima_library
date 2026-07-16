using MediaEngine.Api.Models;
using MediaEngine.Api.Security;
using MediaEngine.Api.Services;
using MediaEngine.Domain;
using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
using MediaEngine.Providers.Helpers;
using MediaEngine.Providers.Workers;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Endpoints;

public static class ItemCanonicalEndpoints
{
    public static IEndpointRouteBuilder MapItemCanonicalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/library/items")
            .WithTags("Items");

        group.MapPut("/{entityId:guid}/preferences", async (
            Guid entityId,
            ItemPreferencesRequest request,
            IMetadataClaimRepository claimRepo,
            ICanonicalValueRepository canonicalRepo,
            ISystemActivityRepository activityRepo,
            IWriteBackService writeBack,
            IEventPublisher publisher,
            IWorkRepository workRepo,
            IItemCanonicalDataService itemCanonicalData,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            if (request.Fields.Count == 0)
                return Results.BadRequest("At least one preference field is required.");

            var context = await itemCanonicalData.ResolveWorkAssetContextAsync(entityId, ct);
            if (context is null)
                return Results.NotFound($"No current media asset or work target found for {entityId}.");

            var now = DateTimeOffset.UtcNow;
            var claims = new List<MetadataClaim>();
            var canonicals = new List<CanonicalValue>();
            var updatedKeys = new List<string>();
            var lineage = await workRepo.GetLineageByAssetAsync(context.AssetId, ct);

            foreach (var (key, value) in request.Fields)
            {
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                    continue;

                var targetId = ResolveScopedTarget(context.AssetId, lineage, key);
                claims.Add(new MetadataClaim
                {
                    Id = Guid.NewGuid(),
                    EntityId = targetId,
                    ProviderId = WellKnownProviders.UserManual,
                    ClaimKey = key,
                    ClaimValue = value,
                    ClaimedAt = now,
                    Confidence = 1.0,
                    IsUserLocked = true,
                });

                canonicals.Add(new CanonicalValue
                {
                    EntityId = targetId,
                    Key = key,
                    Value = value,
                    LastScoredAt = now,
                    IsConflicted = false,
                    NeedsReview = false,
                    WinningProviderId = WellKnownProviders.UserManual,
                });

                updatedKeys.Add(key);
            }

            if (updatedKeys.Count == 0)
                return Results.BadRequest("No valid preference fields were provided.");

            await claimRepo.InsertBatchAsync(claims, ct);
            await canonicalRepo.UpsertBatchAsync(canonicals, ct);

            await activityRepo.LogAsync(new SystemActivityEntry
            {
                OccurredAt = now,
                ActionType = SystemActionType.MetadataManualOverride,
                EntityId = entityId,
                EntityType = "Work",
                CollectionName = context.WorkTitle,
                Detail = $"Saved item preferences for {updatedKeys.Count} field(s): {string.Join(", ", updatedKeys)}.",
            }, ct);

            await publisher.PublishAsync(SignalREvents.MetadataHarvested, new
            {
                entity_id = entityId,
                provider_name = "user_manual",
                updated_fields = updatedKeys.ToArray(),
            }, ct);

            try
            {
                await writeBack.WriteMetadataAsync(context.AssetId, "item_preferences", ct);
            }
            catch (Exception ex)
            {
                loggerFactory
                    .CreateLogger("MediaEngine.Api.Endpoints.ItemCanonicalEndpoints")
                    .LogWarning(ex, "Write-back failed after saving item preferences for {EntityId}.", entityId);
            }

            return Results.Ok(new ItemPreferencesResponse
            {
                EntityId = entityId,
                FieldsUpdated = updatedKeys.Count,
                UpdatedKeys = updatedKeys,
                Message = $"Saved {updatedKeys.Count} preference field(s).",
            });
        })
        .WithName("SaveItemPreferences")
        .WithSummary("Save user-preferred item fields without changing external IDs.")
        .Produces<ItemPreferencesResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        group.MapPut("/{entityId:guid}/display-overrides", async (
            Guid entityId,
            ItemDisplayOverridesRequest request,
            ISystemActivityRepository activityRepo,
            IItemCanonicalDataService itemCanonicalData,
            CancellationToken ct) =>
        {
            if (request.Fields.Count == 0)
                return Results.BadRequest("At least one display override is required.");

            var displayOverrideState = await itemCanonicalData.LoadDisplayOverridesAsync(entityId, ct);
            if (!displayOverrideState.WorkExists)
                return Results.NotFound($"No work found for {entityId}.");

            var current = displayOverrideState.Values;
            var updatedKeys = new List<string>();
            foreach (var (key, value) in request.Fields)
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                var normalizedKey = key.Trim();
                var normalizedValue = (value ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(normalizedValue))
                {
                    if (current.Remove(normalizedKey))
                        updatedKeys.Add(normalizedKey);
                    continue;
                }

                current[normalizedKey] = normalizedValue;
                updatedKeys.Add(normalizedKey);
            }

            if (updatedKeys.Count == 0)
                return Results.BadRequest("No valid display override fields were provided.");

            if (!await itemCanonicalData.SaveDisplayOverridesAsync(entityId, current, ct))
                return Results.NotFound($"No work found for {entityId}.");

            await activityRepo.LogAsync(new SystemActivityEntry
            {
                OccurredAt = DateTimeOffset.UtcNow,
                ActionType = SystemActionType.MetadataManualOverride,
                EntityId = entityId,
                EntityType = "Work",
                Detail = $"Saved display overrides for {updatedKeys.Count} field(s): {string.Join(", ", updatedKeys.Distinct(StringComparer.OrdinalIgnoreCase))}.",
            }, ct);

            return Results.Ok(new ItemDisplayOverridesResponse
            {
                EntityId = entityId,
                FieldsUpdated = updatedKeys.Count,
                UpdatedKeys = updatedKeys.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToList(),
                DisplayOverrides = current,
                Message = $"Saved {updatedKeys.Count} display override field(s).",
            });
        })
        .WithName("SaveItemDisplayOverrides")
        .WithSummary("Save presentation-only display overrides without changing canonical values.")
        .Produces<ItemDisplayOverridesResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        group.MapPost("/{entityId:guid}/canonical-search", async (
            Guid entityId,
            ItemCanonicalSearchRequest request,
            ISearchService searchService,
            IItemCanonicalDataService itemCanonicalData,
            CancellationToken ct) =>
        {
            var context = await itemCanonicalData.ResolveWorkAssetContextAsync(entityId, ct);
            if (context is null)
                return Results.NotFound($"No current media asset or work target found for {entityId}.");

            var mediaType = ResolveMediaType(request.MediaType, context.MediaType);
            var policy = ResolveTargetPolicy(mediaType, request.TargetKind, request.TargetFieldGroup);
            if (policy is null)
                return Results.BadRequest($"Unsupported target field group '{request.TargetFieldGroup}' for media type '{mediaType}'.");

            var draftFields = request.DraftFields
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

            var query = BuildCanonicalQuery(policy, draftFields, request.QueryOverride);
            if (string.IsNullOrWhiteSpace(query))
                return Results.BadRequest("A search query or draft field values are required.");

            var missingRequired = policy.RequiredFieldKeys
                .Where(key => !draftFields.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                .ToList();

            var retailCandidates = new List<ItemCanonicalRetailCandidate>();
            var linkedCandidates = new List<ItemCanonicalLinkedCandidate>();

            var searchMode = NormalizeSearchMode(request.SearchMode);
            var shouldSearchRetail = (searchMode is "retail_only" or "combined") && policy.SearchRetail;
            var shouldSearchUniverse = (searchMode is "wikidata_only" or "combined") && policy.SearchUniverse;

            if (shouldSearchRetail)
            {
                var retail = await searchService.SearchRetailAsync(
                    new Domain.Models.SearchRetailRequest(
                        Query: query,
                        MediaType: mediaType,
                        MaxCandidates: Math.Clamp(request.MaxCandidates, 1, 10),
                        LocalTitle: context.WorkTitle,
                        LocalAuthor: context.PrimaryCreator,
                        LocalYear: context.Year,
                        FileHints: draftFields.Count > 0 ? draftFields : null,
                        SearchFields: draftFields.Count > 0 ? draftFields : null),
                    ct);

                retailCandidates = retail.Candidates
                    .Select(candidate => BuildRetailCandidate(candidate, mediaType, policy))
                    .ToList();
            }

            if (shouldSearchUniverse)
            {
                var universe = await searchService.SearchUniverseAsync(
                    new Domain.Models.SearchUniverseRequest(
                        Query: query,
                        MediaType: mediaType,
                        MaxCandidates: Math.Clamp(request.MaxCandidates, 1, 10),
                        LocalTitle: context.WorkTitle,
                        LocalAuthor: context.PrimaryCreator,
                        LocalYear: context.Year),
                    ct);

                linkedCandidates = universe.Candidates
                    .Select(candidate => BuildLinkedCandidate(candidate, mediaType, policy))
                    .ToList();
            }

            var unlinkedFields = policy.RequiredFieldKeys
                .Concat(policy.SuggestedFieldKeys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(draftFields.ContainsKey)
                .ToDictionary(key => key, key => draftFields[key], StringComparer.OrdinalIgnoreCase);

            return Results.Ok(new ItemCanonicalSearchResponse
            {
                EntityId = entityId,
                MediaType = mediaType,
                TargetKind = policy.TargetKind,
                TargetFieldGroup = policy.TargetFieldGroup,
                Query = query,
                RetailCandidates = retailCandidates,
                LinkedCandidates = linkedCandidates,
                DraftFields = draftFields,
                FallbackActions =
                [
                    "Keep current canonical value",
                    "Save as preference only",
                    "Apply as unlinked canonical value",
                ],
                NoResultMessage = retailCandidates.Count == 0 && linkedCandidates.Count == 0
                    ? searchMode == "wikidata_only"
                        ? "No Wikidata identity was found. Keep the retail match as provider-only, mark Wikidata missing, or try a different query."
                        : "No safe retail result was found. Keep the current value, save the draft as a preference, or apply an unlinked canonical value when the required anchors are present."
                    : null,
                CanApplyUnlinkedCanonical = policy.AllowsTextOnly && missingRequired.Count == 0 && unlinkedFields.Count > 0,
                MissingRequiredFields = missingRequired,
                UnlinkedFields = unlinkedFields,
            });
        })
        .WithName("SearchItemCanonicalCandidates")
        .WithSummary("Run a targeted canonical search for a specific item field group.")
        .Produces<ItemCanonicalSearchResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        group.MapPost("/{entityId:guid}/canonical-apply", async (
            Guid entityId,
            ItemCanonicalApplyRequest request,
            IMetadataClaimRepository claimRepo,
            ICanonicalValueRepository canonicalRepo,
            IBridgeIdRepository bridgeIdRepo,
            ISystemActivityRepository activityRepo,
            IWriteBackService writeBack,
            IEventPublisher publisher,
            ICollectionRepository collectionRepo,
            IWorkRepository workRepo,
            IHydrationPipelineService pipeline,
            TimelineRecorder timeline,
            IItemCanonicalDataService itemCanonicalData,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var context = await itemCanonicalData.ResolveWorkAssetContextAsync(entityId, ct);
            if (context is null)
                return Results.NotFound($"No current media asset or work target found for {entityId}.");

            var policy = ResolveTargetPolicy(context.MediaType, request.TargetKind, request.TargetFieldGroup);
            if (policy is null)
                return Results.BadRequest($"Unsupported target field group '{request.TargetFieldGroup}' for media type '{context.MediaType}'.");

            var now = DateTimeOffset.UtcNow;
            var selectedSuggested = request.AcceptedSuggestedKeys
                .Where(key => request.SuggestedFields.ContainsKey(key))
                .ToDictionary(key => key, key => request.SuggestedFields[key], StringComparer.OrdinalIgnoreCase);
            var selectedFields = request.RequiredFields
                .Concat(selectedSuggested)
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

            if (selectedFields.Count == 0 && request.BridgeIds.Count == 0 && request.QidFields.Count == 0)
                return Results.BadRequest("No canonical data was selected to apply.");

            var claims = new List<MetadataClaim>();
            var canonicals = new List<CanonicalValue>();
            var lineage = await workRepo.GetLineageByAssetAsync(context.AssetId, ct);

            foreach (var (key, value) in selectedFields.Concat(request.QidFields))
            {
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                var targetId = ResolveScopedTarget(context.AssetId, lineage, key);
                claims.Add(new MetadataClaim
                {
                    Id = Guid.NewGuid(),
                    EntityId = targetId,
                    ProviderId = WellKnownProviders.UserManual,
                    ClaimKey = key,
                    ClaimValue = value,
                    ClaimedAt = now,
                    Confidence = 1.0,
                    IsUserLocked = true,
                });

                canonicals.Add(new CanonicalValue
                {
                    EntityId = targetId,
                    Key = key,
                    Value = value,
                    LastScoredAt = now,
                    IsConflicted = false,
                    NeedsReview = false,
                    WinningProviderId = WellKnownProviders.UserManual,
                });
            }

            if (claims.Count > 0)
                await claimRepo.InsertBatchAsync(claims, ct);
            if (canonicals.Count > 0)
                await canonicalRepo.UpsertBatchAsync(canonicals, ct);

            var clearedIds = await ClearStaleIdsAsync(context.AssetId, lineage, policy, request, itemCanonicalData, ct);

            if (request.BridgeIds.Count > 0)
            {
                var bridgeEntries = request.BridgeIds
                    .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                    .Select(kv => new BridgeIdEntry
                    {
                        EntityId = ResolveScopedTarget(context.AssetId, lineage, kv.Key),
                        IdType = kv.Key,
                        IdValue = kv.Value,
                        ProviderId = WellKnownProviders.UserManual.ToString(),
                        CreatedAt = now,
                    })
                    .ToList();

                await bridgeIdRepo.UpsertBatchAsync(bridgeEntries, ct);

                var bridgeClaims = bridgeEntries.Select(entry => new MetadataClaim
                {
                    Id = Guid.NewGuid(),
                    EntityId = entry.EntityId,
                    ProviderId = WellKnownProviders.UserManual,
                    ClaimKey = entry.IdType,
                    ClaimValue = entry.IdValue,
                    ClaimedAt = now,
                    Confidence = 1.0,
                    IsUserLocked = true,
                }).ToList();
                var bridgeCanonicals = bridgeEntries.Select(entry => new CanonicalValue
                {
                    EntityId = entry.EntityId,
                    Key = entry.IdType,
                    Value = entry.IdValue,
                    LastScoredAt = now,
                    IsConflicted = false,
                    NeedsReview = false,
                    WinningProviderId = WellKnownProviders.UserManual,
                }).ToList();

                await claimRepo.InsertBatchAsync(bridgeClaims, ct);
                await canonicalRepo.UpsertBatchAsync(bridgeCanonicals, ct);
            }

            if (request.QidFields.TryGetValue(BridgeIdKeys.WikidataQid, out var globalQid) && !string.IsNullOrWhiteSpace(globalQid))
            {
                var qidTargetId = ResolveScopedTarget(context.AssetId, lineage, BridgeIdKeys.WikidataQid);
                await itemCanonicalData.UpdateWorkIdentityAsync(qidTargetId, globalQid, ct);
                await collectionRepo.UpdateWorkWikidataMatchStateAsync(qidTargetId, WorkWikidataStatus.UserConfirmed, WorkWikidataMatchSource.User, true, globalQid, ct: ct);

                await pipeline.EnqueueAsync(new HarvestRequest
                {
                    EntityId = context.AssetId,
                    EntityType = EntityType.MediaAsset,
                    MediaType = ToMediaType(context.MediaType),
                    Hints = selectedFields,
                    PreResolvedQid = globalQid,
                    IsUserResolution = true,
                    SuppressReviewCreation = true,
                }, ct);
            }
            else if (string.Equals(NormalizeLinkState(request.LinkState), "provider_only", StringComparison.Ordinal))
            {
                await collectionRepo.UpdateWorkWikidataMatchStateAsync(
                    ResolveScopedTarget(context.AssetId, lineage, BridgeIdKeys.WikidataQid),
                    WorkWikidataStatus.ProviderOnly,
                    WorkWikidataMatchSource.Retail,
                    false,
                    ct: ct);

                if (request.BridgeIds.Count > 0)
                {
                    await timeline.RecordRetailMatchedAsync(
                        context.AssetId,
                        request.ProviderName ?? WellKnownProviders.UserManual.ToString(),
                        Math.Max(1, selectedFields.Count + request.BridgeIds.Count),
                        ct: ct);

                    await pipeline.EnqueueAsync(new HarvestRequest
                    {
                        EntityId = context.AssetId,
                        EntityType = EntityType.MediaAsset,
                        MediaType = ToMediaType(context.MediaType),
                        Hints = selectedFields,
                        SkipRetailStage = true,
                        IsUserResolution = true,
                    }, ct);
                }
            }

            await activityRepo.LogAsync(new SystemActivityEntry
            {
                OccurredAt = now,
                ActionType = SystemActionType.MetadataManualOverride,
                EntityId = entityId,
                EntityType = "Work",
                CollectionName = context.WorkTitle,
                Detail = $"Applied canonical {policy.TargetFieldGroup} selection ({NormalizeLinkState(request.LinkState)}) with {selectedFields.Count} field(s).",
            }, ct);

            await publisher.PublishAsync(SignalREvents.MetadataHarvested, new
            {
                entity_id = entityId,
                provider_name = "user_manual",
                updated_fields = selectedFields.Keys
                    .Concat(request.QidFields.Keys)
                    .Concat(request.BridgeIds.Keys)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
            }, ct);

            try
            {
                await writeBack.WriteMetadataAsync(context.AssetId, "item_canonical_apply", ct);
            }
            catch (Exception ex)
            {
                loggerFactory
                    .CreateLogger("MediaEngine.Api.Endpoints.ItemCanonicalEndpoints")
                    .LogWarning(ex, "Write-back failed after applying canonical candidate for {EntityId}.", entityId);
            }

            return Results.Ok(new ItemCanonicalApplyResponse
            {
                EntityId = entityId,
                LinkState = NormalizeLinkState(request.LinkState),
                LinkStatusLabel = GetLinkStatusLabel(request.LinkState),
                FieldsApplied = selectedFields.Count,
                IdsCleared = clearedIds,
                Message = $"Applied canonical {policy.TargetFieldGroup} fields.",
            });
        })
        .WithName("ApplyItemCanonicalCandidate")
        .WithSummary("Apply a targeted canonical candidate and clear stale IDs for the same field group.")
        .Produces<ItemCanonicalApplyResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        group.MapPost("/{entityId:guid}/retail-match", async (
            Guid entityId,
            ReplaceRetailMatchRequest request,
            IMetadataClaimRepository claimRepo,
            ICanonicalValueRepository canonicalRepo,
            IBridgeIdRepository bridgeIdRepo,
            ISystemActivityRepository activityRepo,
            IEventPublisher publisher,
            ICollectionRepository collectionRepo,
            IWorkRepository workRepo,
            IReviewQueueRepository reviewRepo,
            IHydrationPipelineService pipeline,
            CoverArtWorker coverArtWorker,
            TimelineRecorder timeline,
            IItemCanonicalDataService itemCanonicalData,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var context = await itemCanonicalData.ResolveWorkAssetContextAsync(entityId, ct);
            if (context is null)
                return Results.NotFound($"No current media asset or work target found for {entityId}.");

            if (string.IsNullOrWhiteSpace(request.ProviderName) || string.IsNullOrWhiteSpace(request.ProviderItemId))
                return Results.BadRequest("Provider name and provider item ID are required.");

            if (!Guid.TryParse(request.ProviderId, out var providerId))
                return Results.BadRequest("A valid provider ID is required for a retail replacement.");

            var policy = ResolveTargetPolicy(context.MediaType, request.TargetKind, request.TargetFieldGroup);
            if (policy is null)
                return Results.BadRequest($"Unsupported target field group '{request.TargetFieldGroup}' for media type '{context.MediaType}'.");

            var now = DateTimeOffset.UtcNow;
            var lineage = await workRepo.GetLineageByAssetAsync(context.AssetId, ct);
            if (lineage is null)
                return Results.NotFound($"No work lineage found for {entityId}.");
            var allowedFieldKeys = policy.RequiredFieldKeys
                .Concat(policy.SuggestedFieldKeys)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var selectedFields = request.RequiredFields
                .Concat(request.SuggestedFields)
                .Where(kv => allowedFieldKeys.Contains(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

            var missingRequiredFields = policy.RequiredFieldKeys
                .Where(key => !selectedFields.ContainsKey(key))
                .ToList();
            if (missingRequiredFields.Count > 0)
                return Results.BadRequest($"Retail match is missing required fields: {string.Join(", ", missingRequiredFields)}.");

            var selectedBridgeIds = request.BridgeIds
                .Where(kv => policy.BridgeIdKeys.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)
                             && !string.IsNullOrWhiteSpace(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

            var parentConflict = await FindChildParentIdentityConflictAsync(
                policy,
                lineage,
                selectedFields,
                selectedBridgeIds,
                canonicalRepo,
                bridgeIdRepo,
                ct);
            if (!string.IsNullOrWhiteSpace(parentConflict))
                return Results.Conflict(parentConflict);

            var staleBridgeKeys = policy.BridgeIdKeys
                .Where(key => !selectedBridgeIds.ContainsKey(key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (staleBridgeKeys.Count > 0)
            {
                await itemCanonicalData.DeleteIdentityArtifactsAsync(
                    staleBridgeKeys.Select(key => new ItemCanonicalIdentityArtifact(
                        ResolvePolicyScopedTarget(context.AssetId, lineage, policy, key),
                        key)).ToList(),
                    ct);
            }

            var claims = selectedFields.Select(kv => new MetadataClaim
            {
                Id = Guid.NewGuid(),
                EntityId = ResolvePolicyScopedTarget(context.AssetId, lineage, policy, kv.Key),
                ProviderId = providerId,
                DecisionSourceProviderId = WellKnownProviders.UserManual,
                ClaimKey = kv.Key,
                ClaimValue = kv.Value,
                ClaimedAt = now,
                Confidence = 1.0,
                IsUserLocked = false,
            }).ToList();

            var canonicals = selectedFields.Select(kv => new CanonicalValue
            {
                EntityId = ResolvePolicyScopedTarget(context.AssetId, lineage, policy, kv.Key),
                Key = kv.Key,
                Value = kv.Value,
                LastScoredAt = now,
                IsConflicted = false,
                NeedsReview = false,
                WinningProviderId = providerId,
            }).ToList();

            var identityTarget = ResolvePolicyIdentityTarget(context.AssetId, lineage, policy);
            claims.AddRange(
            [
                new MetadataClaim
                {
                    Id = Guid.NewGuid(),
                    EntityId = identityTarget,
                    ProviderId = providerId,
                    DecisionSourceProviderId = WellKnownProviders.UserManual,
                    ClaimKey = MetadataFieldConstants.IdentityProvider,
                    ClaimValue = request.ProviderName,
                    ClaimedAt = now,
                    Confidence = 1.0,
                },
                new MetadataClaim
                {
                    Id = Guid.NewGuid(),
                    EntityId = identityTarget,
                    ProviderId = providerId,
                    DecisionSourceProviderId = WellKnownProviders.UserManual,
                    ClaimKey = MetadataFieldConstants.IdentityProviderItemId,
                    ClaimValue = request.ProviderItemId,
                    ClaimedAt = now,
                    Confidence = 1.0,
                },
            ]);
            canonicals.AddRange(
            [
                new CanonicalValue
                {
                    EntityId = identityTarget,
                    Key = MetadataFieldConstants.IdentityProvider,
                    Value = request.ProviderName,
                    LastScoredAt = now,
                    WinningProviderId = providerId,
                },
                new CanonicalValue
                {
                    EntityId = identityTarget,
                    Key = MetadataFieldConstants.IdentityProviderItemId,
                    Value = request.ProviderItemId,
                    LastScoredAt = now,
                    WinningProviderId = providerId,
                },
            ]);

            await claimRepo.InsertBatchAsync(claims, ct);
            await canonicalRepo.UpsertBatchAsync(canonicals, ct);

            if (selectedBridgeIds.Count > 0)
            {
                var bridgeEntries = selectedBridgeIds
                    .Select(kv => new BridgeIdEntry
                    {
                        EntityId = ResolvePolicyScopedTarget(context.AssetId, lineage, policy, kv.Key),
                        IdType = kv.Key,
                        IdValue = kv.Value,
                        ProviderId = request.ProviderName,
                        CreatedAt = now,
                    })
                    .ToList();

                await bridgeIdRepo.UpsertBatchAsync(bridgeEntries, ct);

                var bridgeClaims = bridgeEntries.Select(entry => new MetadataClaim
                {
                    Id = Guid.NewGuid(),
                    EntityId = entry.EntityId,
                    ProviderId = providerId,
                    DecisionSourceProviderId = WellKnownProviders.UserManual,
                    ClaimKey = entry.IdType,
                    ClaimValue = entry.IdValue,
                    ClaimedAt = now,
                    Confidence = 1.0,
                }).ToList();
                var bridgeCanonicals = bridgeEntries.Select(entry => new CanonicalValue
                {
                    EntityId = entry.EntityId,
                    Key = entry.IdType,
                    Value = entry.IdValue,
                    LastScoredAt = now,
                    IsConflicted = false,
                    NeedsReview = false,
                    WinningProviderId = providerId,
                }).ToList();

                await claimRepo.InsertBatchAsync(bridgeClaims, ct);
                await canonicalRepo.UpsertBatchAsync(bridgeCanonicals, ct);
            }

            await ReplaceScopedExternalIdentifiersAsync(
                lineage,
                policy,
                staleBridgeKeys,
                selectedBridgeIds,
                itemCanonicalData,
                ct);

            RetailArtworkReplacementResult? artworkResult = null;
            if (SupportsImmediateRetailArtworkReplacement(policy))
            {
                if (!string.IsNullOrWhiteSpace(request.CoverUrl))
                {
                    await claimRepo.InsertBatchAsync(
                    [
                        new MetadataClaim
                        {
                            Id = Guid.NewGuid(),
                            EntityId = ResolvePolicyScopedTarget(context.AssetId, lineage, policy, MetadataFieldConstants.Cover),
                            ProviderId = providerId,
                            DecisionSourceProviderId = WellKnownProviders.UserManual,
                            ClaimKey = MetadataFieldConstants.Cover,
                            ClaimValue = request.CoverUrl.Trim(),
                            ClaimedAt = now,
                            Confidence = 1.0,
                        },
                    ], ct);
                }

                artworkResult = await coverArtWorker.ReplaceProviderArtworkAsync(
                    context.AssetId,
                    request.CoverUrl,
                    request.ProviderName,
                    providerId,
                    ct);
            }

            var workId = ResolvePolicyWorkTarget(lineage, policy, BridgeIdKeys.WikidataQid);
            var currentState = await itemCanonicalData.LoadWorkWikidataStateAsync(workId, ct);
            if (request.ClearAutoAlignedWikidata
                && !string.IsNullOrWhiteSpace(currentState?.Qid)
                && IsAutomationOwnedWikidataState(currentState.Status, currentState.Source, currentState.Locked))
            {
                await collectionRepo.UpdateWorkWikidataMatchStateAsync(workId, WorkWikidataStatus.Pending, WorkWikidataMatchSource.Retail, false, "", ct: ct);
            }
            else
            {
                await collectionRepo.UpdateWorkWikidataMatchStateAsync(workId, WorkWikidataStatus.ProviderOnly, WorkWikidataMatchSource.Retail, false, ct: ct);
            }

            if (request.ReviewItemId is { } reviewItemId)
                await reviewRepo.UpdateStatusAsync(reviewItemId, ReviewStatus.Resolved, "user", ct);

            await activityRepo.LogAsync(new SystemActivityEntry
            {
                OccurredAt = now,
                ActionType = SystemActionType.MetadataManualOverride,
                EntityId = entityId,
                EntityType = "Work",
                CollectionName = context.WorkTitle,
                Detail = $"Retail match replaced with {request.ProviderName} {request.ProviderItemId}.",
            }, ct);

            await timeline.RecordRetailMatchedAsync(
                context.AssetId,
                request.ProviderName,
                Math.Max(1, selectedFields.Count + request.BridgeIds.Count),
                ct: ct);

            var identityJobId = await pipeline.EnqueueAsync(new HarvestRequest
            {
                EntityId = context.AssetId,
                EntityType = EntityType.MediaAsset,
                MediaType = ToMediaType(context.MediaType),
                Hints = selectedFields
                    .Append(new KeyValuePair<string, string>(MetadataFieldConstants.IdentityProvider, request.ProviderName))
                    .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
                SkipRetailStage = true,
                IsUserResolution = true,
            }, ct);

            await activityRepo.LogAsync(new SystemActivityEntry
            {
                OccurredAt = DateTimeOffset.UtcNow,
                ActionType = SystemActionType.MediaUpdated,
                EntityId = context.AssetId,
                EntityType = "MediaAsset",
                CollectionName = context.WorkTitle,
                Detail = artworkResult is null
                    ? $"Retail identity changed to {request.ProviderName} {request.ProviderItemId}."
                    : $"Retail identity changed to {request.ProviderName} {request.ProviderItemId}. {artworkResult.Message}",
            }, ct);
            await activityRepo.LogAsync(new SystemActivityEntry
            {
                OccurredAt = DateTimeOffset.UtcNow,
                ActionType = SystemActionType.HydrationEnqueued,
                EntityId = context.AssetId,
                EntityType = "MediaAsset",
                CollectionName = context.WorkTitle,
                Detail = $"Queued identity job {identityJobId} for post-retail Wikidata alignment.",
            }, ct);

            loggerFactory
                .CreateLogger("MediaEngine.Api.Endpoints.ItemCanonicalEndpoints")
                .LogInformation(
                    "Manual retail update completed for {Title} ({AssetId}): provider={Provider}, providerItemId={ProviderItemId}, artworkChanged={ArtworkChanged}, identityJobId={IdentityJobId}",
                    context.WorkTitle,
                    context.AssetId,
                    request.ProviderName,
                    request.ProviderItemId,
                    artworkResult?.ArtworkChanged ?? false,
                    identityJobId);

            await publisher.PublishAsync(SignalREvents.MetadataHarvested, new
            {
                entity_id = entityId,
                target_scope_id = request.TargetScopeId,
                target_field_group = policy.TargetFieldGroup,
                provider_name = request.ProviderName,
                provider_item_id = request.ProviderItemId,
                updated_fields = selectedFields.Keys
                    .Concat(request.BridgeIds.Keys)
                    .Concat(artworkResult?.ArtworkChanged == true
                        ? [MetadataFieldConstants.Cover, MetadataFieldConstants.CoverUrl, MetadataFieldConstants.CoverState]
                        : [])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
            }, ct);

            return Results.Ok(new ItemCanonicalApplyResponse
            {
                EntityId = entityId,
                LinkState = "provider_only",
                LinkStatusLabel = artworkResult?.CoverDownloaded == true
                    ? "Retail match and artwork applied; Wikidata alignment queued."
                    : "Retail match applied; Wikidata alignment queued.",
                FieldsApplied = selectedFields.Count,
                IdsCleared = staleBridgeKeys,
                Message = artworkResult?.CoverDownloaded == true
                    ? "Retail match and artwork applied; Wikidata alignment queued."
                    : "Retail match applied; Wikidata alignment queued.",
                IdentityJobId = identityJobId,
                PipelineState = IdentityJobState.RetailMatched.ToString(),
                ArtworkChanged = artworkResult?.ArtworkChanged ?? false,
                ArtworkRemovedCount = artworkResult?.RemovedVariantCount ?? 0,
                ArtworkMessage = artworkResult?.Message,
            });
        })
        .WithName("ReplaceItemRetailMatch")
        .WithSummary("Replace or confirm the item retail/provider match.")
        .Produces<ItemCanonicalApplyResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        group.MapPost("/{entityId:guid}/wikidata-match", async (
            Guid entityId,
            ReplaceWikidataMatchRequest request,
            IMetadataClaimRepository claimRepo,
            ICanonicalValueRepository canonicalRepo,
            IHydrationPipelineService pipeline,
            ISystemActivityRepository activityRepo,
            IEventPublisher publisher,
            ICollectionRepository collectionRepo,
            IWorkRepository workRepo,
            IReviewQueueRepository reviewRepo,
            IItemCanonicalDataService itemCanonicalData,
            CancellationToken ct) =>
        {
            var context = await itemCanonicalData.ResolveWorkAssetContextAsync(entityId, ct);
            if (context is null)
                return Results.NotFound($"No current media asset or work target found for {entityId}.");

            var action = (request.Action ?? "replace").Trim().ToLowerInvariant();
            var policy = ResolveTargetPolicy(context.MediaType, request.TargetKind, request.TargetFieldGroup);
            if (policy is null)
                return Results.BadRequest($"Unsupported target field group '{request.TargetFieldGroup}' for media type '{context.MediaType}'.");
            var lineage = await workRepo.GetLineageByAssetAsync(context.AssetId, ct);
            if (lineage is null)
                return Results.NotFound($"No work lineage found for {entityId}.");
            var workId = ResolvePolicyWorkTarget(lineage, policy, BridgeIdKeys.WikidataQid);
            var now = DateTimeOffset.UtcNow;
            var fieldsApplied = 0;
            Guid? identityJobId = null;
            var message = action switch
            {
                "replace" => "Wikidata identity replaced; enrichment queued.",
                "clear" => "Wikidata identity cleared; retail match kept.",
                "mark_missing" => "Retail match kept; Wikidata marked missing.",
                "reject" => "Wikidata identity rejected; retail match kept.",
                _ => "Wikidata identity updated.",
            };

            if (action == "replace")
            {
                if (string.IsNullOrWhiteSpace(request.Qid))
                    return Results.BadRequest("QID is required when replacing a Wikidata match.");

                var qid = request.Qid.Trim();
                await claimRepo.InsertBatchAsync([new MetadataClaim
                {
                    Id = Guid.NewGuid(),
                    EntityId = workId,
                    ProviderId = WellKnownProviders.Wikidata,
                    DecisionSourceProviderId = WellKnownProviders.UserManual,
                    ClaimKey = BridgeIdKeys.WikidataQid,
                    ClaimValue = qid,
                    Confidence = 1.0,
                    ClaimedAt = now,
                }], ct);

                await canonicalRepo.UpsertBatchAsync([new CanonicalValue
                {
                    EntityId = workId,
                    Key = BridgeIdKeys.WikidataQid,
                    Value = qid,
                    LastScoredAt = now,
                    IsConflicted = false,
                    NeedsReview = false,
                    WinningProviderId = WellKnownProviders.Wikidata,
                }], ct);

                await collectionRepo.UpdateWorkWikidataMatchStateAsync(workId, WorkWikidataStatus.UserReplaced, WorkWikidataMatchSource.User, true, qid, ct: ct);
                fieldsApplied = 1;

                if (request.RehydrateNow)
                {
                    var canonicals = await canonicalRepo.GetByEntityAsync(workId, ct);
                    var hints = canonicals.ToDictionary(c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase);
                    identityJobId = await pipeline.EnqueueAsync(new HarvestRequest
                    {
                        EntityId = context.AssetId,
                        EntityType = EntityType.MediaAsset,
                        MediaType = ToMediaType(context.MediaType),
                        Hints = hints,
                        PreResolvedQid = qid,
                        SuppressReviewCreation = true,
                        IsUserResolution = true,
                    }, ct);
                }
            }
            else if (action == "clear")
            {
                await collectionRepo.UpdateWorkWikidataMatchStateAsync(workId, WorkWikidataStatus.ProviderOnly, WorkWikidataMatchSource.User, false, "", ct: ct);
            }
            else if (action == "mark_missing")
            {
                await collectionRepo.UpdateWorkWikidataMatchStateAsync(workId, WorkWikidataStatus.Missing, WorkWikidataMatchSource.User, false, "", ct: ct);
            }
            else if (action == "reject")
            {
                var rejected = await itemCanonicalData.AppendRejectedQidAsync(workId, request.RejectedQid ?? request.Qid, ct);
                await collectionRepo.UpdateWorkWikidataMatchStateAsync(workId, WorkWikidataStatus.UserRejected, WorkWikidataMatchSource.User, false, "", rejected, ct);
            }
            else
            {
                return Results.BadRequest("Unsupported Wikidata match action.");
            }

            if (request.ReviewItemId is { } reviewItemId)
                await reviewRepo.UpdateStatusAsync(reviewItemId, ReviewStatus.Resolved, "user", ct);

            await activityRepo.LogAsync(new SystemActivityEntry
            {
                OccurredAt = now,
                ActionType = SystemActionType.MetadataManualOverride,
                EntityId = entityId,
                EntityType = "Work",
                CollectionName = context.WorkTitle,
                Detail = $"Wikidata match action '{action}' applied. QID: {request.Qid ?? request.RejectedQid ?? "none"}.",
            }, ct);

            await publisher.PublishAsync(SignalREvents.MetadataHarvested, new
            {
                entity_id = entityId,
                target_scope_id = request.TargetScopeId,
                target_field_group = policy.TargetFieldGroup,
                provider_name = "user_manual",
                updated_fields = new[] { BridgeIdKeys.WikidataQid, "wikidata_status" },
            }, ct);

            return Results.Ok(new ItemCanonicalApplyResponse
            {
                EntityId = entityId,
                LinkState = action == "replace" ? "linked" : "provider_only",
                LinkStatusLabel = message,
                FieldsApplied = fieldsApplied,
                Message = message,
                IdentityJobId = identityJobId,
                PipelineState = identityJobId.HasValue ? IdentityJobState.QidResolved.ToString() : null,
            });
        })
        .WithName("ReplaceItemWikidataMatch")
        .WithSummary("Replace, clear, reject, or mark missing the item Wikidata identity.")
        .Produces<ItemCanonicalApplyResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        return app;
    }

    private sealed record CanonicalTargetPolicy(
        string MediaType,
        string TargetKind,
        string TargetFieldGroup,
        IReadOnlyList<string> RequiredFieldKeys,
        IReadOnlyList<string> SuggestedFieldKeys,
        IReadOnlyList<string> BridgeIdKeys,
        IReadOnlyList<string> QidFieldKeys,
        IReadOnlyList<string> QueryFieldKeys,
        bool SearchRetail,
        bool SearchUniverse,
        bool AllowsTextOnly);

    private static bool IsAutomationOwnedWikidataState(string? status, string? source, bool locked) =>
        !locked
        && !string.Equals(source, "user", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(status, WorkWikidataStatus.UserConfirmed, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(status, WorkWikidataStatus.UserReplaced, StringComparison.OrdinalIgnoreCase);

    private static string ResolveMediaType(string? requestedMediaType, string fallbackMediaType) =>
        !string.IsNullOrWhiteSpace(requestedMediaType)
            ? requestedMediaType
            : (string.IsNullOrWhiteSpace(fallbackMediaType) ? MediaType.Unknown.ToString() : fallbackMediaType);

    private static MediaType ToMediaType(string? mediaType) =>
        Enum.TryParse<MediaType>(mediaType, true, out var parsed)
            ? parsed
            : MediaType.Unknown;

    private static CanonicalTargetPolicy? ResolveTargetPolicy(string mediaType, string targetKind, string targetFieldGroup) =>
        (mediaType.Trim(), targetFieldGroup.Trim().ToLowerInvariant()) switch
        {
            ("Music", "album") => new CanonicalTargetPolicy(mediaType, "container", "album",
                [MetadataFieldConstants.Artist, MetadataFieldConstants.Album],
                [MetadataFieldConstants.Year, MetadataFieldConstants.Genre, MetadataFieldConstants.Description, "album_artist"],
                [BridgeIdKeys.AppleArtistId, BridgeIdKeys.AppleMusicCollectionId, BridgeIdKeys.MusicBrainzId, BridgeIdKeys.MusicBrainzReleaseId, BridgeIdKeys.MusicBrainzReleaseGroupId],
                [BridgeIdKeys.WikidataQid],
                [MetadataFieldConstants.Artist, MetadataFieldConstants.Album],
                true, true, true),
            ("Music", "track") => new CanonicalTargetPolicy(mediaType, "item", "track",
                [MetadataFieldConstants.Title, MetadataFieldConstants.Artist, MetadataFieldConstants.Album],
                [MetadataFieldConstants.TrackNumber, MetadataFieldConstants.DurationField, MetadataFieldConstants.Composer, MetadataFieldConstants.Year, "disc_number"],
                [BridgeIdKeys.AppleMusicId, BridgeIdKeys.MusicBrainzRecordingId, BridgeIdKeys.MusicBrainzWorkId, BridgeIdKeys.Isrc],
                [BridgeIdKeys.WikidataQid],
                [MetadataFieldConstants.Title, MetadataFieldConstants.Artist, MetadataFieldConstants.Album],
                true, true, true),
            ("Music", "artist") => new CanonicalTargetPolicy(mediaType, "person", "artist",
                [MetadataFieldConstants.Artist],
                [MetadataFieldConstants.Genre],
                [BridgeIdKeys.AppleArtistId, BridgeIdKeys.MusicBrainzId],
                ["artist_qid"],
                [MetadataFieldConstants.Artist],
                true, true, true),
            ("Audiobooks", "narrator") => new CanonicalTargetPolicy(mediaType, "person", "narrator",
                [MetadataFieldConstants.Narrator],
                [MetadataFieldConstants.Title, MetadataFieldConstants.Author, MetadataFieldConstants.Series, MetadataFieldConstants.SeriesPosition],
                [],
                ["narrator_qid"],
                [MetadataFieldConstants.Narrator, MetadataFieldConstants.Title, MetadataFieldConstants.Author],
                false, true, true),
            ("Audiobooks", "series") => new CanonicalTargetPolicy(mediaType, "container", "series",
                [MetadataFieldConstants.Series],
                [MetadataFieldConstants.SeriesPosition, MetadataFieldConstants.Title, MetadataFieldConstants.Author, MetadataFieldConstants.Year],
                [BridgeIdKeys.AudibleId, BridgeIdKeys.Isbn, BridgeIdKeys.Asin],
                ["series_qid"],
                [MetadataFieldConstants.Series, MetadataFieldConstants.Author],
                true, true, true),
            ("Audiobooks", "audiobook_identity") => new CanonicalTargetPolicy(mediaType, string.IsNullOrWhiteSpace(targetKind) ? "item" : targetKind, "audiobook_identity",
                [MetadataFieldConstants.Title, MetadataFieldConstants.Author, MetadataFieldConstants.Narrator],
                [MetadataFieldConstants.Series, MetadataFieldConstants.SeriesPosition, MetadataFieldConstants.Year, MetadataFieldConstants.PublisherField, MetadataFieldConstants.DurationField, MetadataFieldConstants.Genre],
                [BridgeIdKeys.AudibleId, BridgeIdKeys.Isbn, BridgeIdKeys.Asin],
                [BridgeIdKeys.WikidataQid],
                [MetadataFieldConstants.Title, MetadataFieldConstants.Author, MetadataFieldConstants.Narrator],
                true, true, true),
            ("Books", "series") => new CanonicalTargetPolicy(mediaType, "container", "series",
                [MetadataFieldConstants.Series],
                [MetadataFieldConstants.SeriesPosition, MetadataFieldConstants.Title, MetadataFieldConstants.Author, MetadataFieldConstants.Year],
                [BridgeIdKeys.Isbn, BridgeIdKeys.Asin],
                ["series_qid"],
                [MetadataFieldConstants.Series, MetadataFieldConstants.Author],
                true, true, true),
            ("Books", "book_identity") => new CanonicalTargetPolicy(mediaType, string.IsNullOrWhiteSpace(targetKind) ? "item" : targetKind, "book_identity",
                [MetadataFieldConstants.Title, MetadataFieldConstants.Author],
                [MetadataFieldConstants.Series, MetadataFieldConstants.SeriesPosition, MetadataFieldConstants.Year, MetadataFieldConstants.PublisherField, MetadataFieldConstants.Genre, MetadataFieldConstants.Language],
                [BridgeIdKeys.Isbn, BridgeIdKeys.Asin, BridgeIdKeys.AppleBooksId, BridgeIdKeys.OpenLibraryId],
                [BridgeIdKeys.WikidataQid],
                [MetadataFieldConstants.Title, MetadataFieldConstants.Author, MetadataFieldConstants.Series],
                true, true, true),
            ("Movies", "movie_identity") => new CanonicalTargetPolicy(mediaType, string.IsNullOrWhiteSpace(targetKind) ? "item" : targetKind, "movie_identity",
                [MetadataFieldConstants.Title, MetadataFieldConstants.Year],
                [MetadataFieldConstants.Director, MetadataFieldConstants.OriginalTitle, MetadataFieldConstants.Runtime, MetadataFieldConstants.Genre, MetadataFieldConstants.Composer, MetadataFieldConstants.CastMember],
                [BridgeIdKeys.TmdbId, BridgeIdKeys.ImdbId],
                [BridgeIdKeys.WikidataQid],
                [MetadataFieldConstants.Title, MetadataFieldConstants.Year, MetadataFieldConstants.Director],
                true, true, true),
            ("TV", "show") => new CanonicalTargetPolicy(mediaType, "container", "show",
                [MetadataFieldConstants.ShowName],
                [MetadataFieldConstants.Year, MetadataFieldConstants.Network],
                [BridgeIdKeys.TmdbId, BridgeIdKeys.ImdbId],
                [BridgeIdKeys.WikidataQid],
                [MetadataFieldConstants.ShowName, MetadataFieldConstants.Year],
                true, true, true),
            ("TV", "show_episode") => new CanonicalTargetPolicy(mediaType, string.IsNullOrWhiteSpace(targetKind) ? "item" : targetKind, "show_episode",
                [MetadataFieldConstants.ShowName, MetadataFieldConstants.SeasonNumber, MetadataFieldConstants.EpisodeNumber],
                [MetadataFieldConstants.EpisodeTitle, MetadataFieldConstants.Year, MetadataFieldConstants.Runtime, MetadataFieldConstants.Director, MetadataFieldConstants.CastMember],
                [BridgeIdKeys.TmdbId, BridgeIdKeys.TmdbEpisodeId, BridgeIdKeys.ImdbId],
                [BridgeIdKeys.WikidataQid],
                [MetadataFieldConstants.ShowName, MetadataFieldConstants.SeasonNumber, MetadataFieldConstants.EpisodeNumber, MetadataFieldConstants.EpisodeTitle],
                true, true, true),
            ("Comics", "series") => new CanonicalTargetPolicy(mediaType, "container", "series",
                [MetadataFieldConstants.Series],
                [MetadataFieldConstants.SeriesPosition, MetadataFieldConstants.Title, MetadataFieldConstants.Year, MetadataFieldConstants.Author, MetadataFieldConstants.Illustrator, MetadataFieldConstants.PublisherField, MetadataFieldConstants.Genre],
                [BridgeIdKeys.ComicVineId, BridgeIdKeys.Isbn],
                ["series_qid"],
                [MetadataFieldConstants.Series, MetadataFieldConstants.SeriesPosition, MetadataFieldConstants.Title],
                true, true, true),
            ("Comics", "issue") => new CanonicalTargetPolicy(mediaType, string.IsNullOrWhiteSpace(targetKind) ? "item" : targetKind, "issue",
                [MetadataFieldConstants.Series, MetadataFieldConstants.SeriesPosition],
                [MetadataFieldConstants.Title, MetadataFieldConstants.Year, MetadataFieldConstants.Author, MetadataFieldConstants.Illustrator, MetadataFieldConstants.PublisherField, MetadataFieldConstants.Genre],
                [BridgeIdKeys.ComicVineId, BridgeIdKeys.Isbn],
                [BridgeIdKeys.WikidataQid],
                [MetadataFieldConstants.Series, MetadataFieldConstants.SeriesPosition, MetadataFieldConstants.Title],
                true, true, true),
            _ => null,
        };

    private static string BuildCanonicalQuery(CanonicalTargetPolicy policy, IReadOnlyDictionary<string, string> draftFields, string? queryOverride)
    {
        if (!string.IsNullOrWhiteSpace(queryOverride))
            return queryOverride.Trim();

        return string.Join(" ", policy.QueryFieldKeys
            .Where(draftFields.ContainsKey)
            .Select(key => draftFields[key].Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static ItemCanonicalRetailCandidate BuildRetailCandidate(
        Domain.Models.RetailCandidate candidate,
        string mediaType,
        CanonicalTargetPolicy policy)
    {
        var allFields = BuildRetailFieldBag(candidate, mediaType, policy.TargetFieldGroup);
        var allowContainerTitleAliases = IsContainerIdentityPolicy(policy);
        var requiredFields = ExtractFields(allFields, policy.RequiredFieldKeys, allowContainerTitleAliases);
        var suggestedFields = ExtractFields(allFields, policy.SuggestedFieldKeys, allowContainerTitleAliases);
        var bridgeIds = ExtractFields(allFields, policy.BridgeIdKeys);
        var missingRequired = policy.RequiredFieldKeys.Where(key => !requiredFields.ContainsKey(key)).ToList();

        return new ItemCanonicalRetailCandidate
        {
            CandidateId = $"{candidate.ProviderName}:{candidate.ProviderItemId ?? candidate.Title}",
            ProviderId = candidate.ProviderId,
            ProviderName = candidate.ProviderName,
            ProviderItemId = candidate.ProviderItemId,
            Title = candidate.Title,
            Year = candidate.Year,
            Author = candidate.Author,
            Director = candidate.Director,
            Description = candidate.Description,
            CoverUrl = candidate.CoverUrl,
            Confidence = candidate.Confidence,
            CompositeScore = candidate.CompositeScore,
            ExtraFields = candidate.ExtraFields?.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            LinkState = "provider_only",
            LinkStatusLabel = "Linked to provider only",
            IsApplicable = missingRequired.Count == 0,
            BlockedReason = missingRequired.Count == 0 ? null : $"Missing required anchors: {string.Join(", ", missingRequired)}.",
            RequiredFields = requiredFields,
            SuggestedFields = suggestedFields,
            BridgeIds = bridgeIds,
            QidFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        };
    }

    private static ItemCanonicalLinkedCandidate BuildLinkedCandidate(
        Domain.Models.UniverseCandidate candidate,
        string mediaType,
        CanonicalTargetPolicy policy)
    {
        var allFields = BuildUniverseFieldBag(candidate, mediaType, policy.TargetFieldGroup);
        var requiredFields = ExtractFields(allFields, policy.RequiredFieldKeys);
        var suggestedFields = ExtractFields(allFields, policy.SuggestedFieldKeys);
        var qidFields = policy.QidFieldKeys.ToDictionary(key => key, _ => candidate.Qid, StringComparer.OrdinalIgnoreCase);
        var missingRequired = policy.RequiredFieldKeys.Where(key => !requiredFields.ContainsKey(key)).ToList();

        return new ItemCanonicalLinkedCandidate
        {
            CandidateId = $"wikidata:{candidate.Qid}",
            Qid = candidate.Qid,
            Label = candidate.Label,
            Description = candidate.Description,
            InstanceOf = candidate.InstanceOf,
            Year = candidate.Year,
            Author = candidate.Author,
            Director = candidate.Director,
            CoverUrl = candidate.CoverUrl,
            WikipediaExtract = candidate.WikipediaExtract,
            ResolutionTier = candidate.ResolutionTier,
            Confidence = candidate.Confidence,
            BridgeIds = candidate.BridgeIds?.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            MediaTypeMetadata = candidate.MediaTypeMetadata?.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            LinkState = "linked",
            LinkStatusLabel = "Linked to Wikidata",
            IsApplicable = missingRequired.Count == 0,
            BlockedReason = missingRequired.Count == 0 ? null : $"Missing required anchors: {string.Join(", ", missingRequired)}.",
            RequiredFields = requiredFields,
            SuggestedFields = suggestedFields,
            QidFields = qidFields,
        };
    }

    private static Dictionary<string, string> BuildRetailFieldBag(Domain.Models.RetailCandidate candidate, string mediaType, string targetFieldGroup)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(candidate.Title))
            fields[MetadataFieldConstants.Title] = candidate.Title;
        if (!string.IsNullOrWhiteSpace(candidate.Author))
        {
            fields[MetadataFieldConstants.Author] = candidate.Author;
            fields.TryAdd(MetadataFieldConstants.Artist, candidate.Author);
        }
        if (!string.IsNullOrWhiteSpace(candidate.Director))
            fields[MetadataFieldConstants.Director] = candidate.Director;
        if (!string.IsNullOrWhiteSpace(candidate.Description))
            fields[MetadataFieldConstants.Description] = candidate.Description;
        if (!string.IsNullOrWhiteSpace(candidate.Year))
            fields[MetadataFieldConstants.Year] = candidate.Year;
        if (!string.IsNullOrWhiteSpace(candidate.CoverUrl))
            fields[MetadataFieldConstants.CoverUrl] = candidate.CoverUrl;
        if (!string.IsNullOrWhiteSpace(candidate.ProviderItemId))
            fields["provider_item_id"] = candidate.ProviderItemId;

        foreach (var (key, value) in candidate.ExtraFields ?? new Dictionary<string, string>())
        {
            if (!string.IsNullOrWhiteSpace(value))
                fields[key] = value;
        }

        if (!string.IsNullOrWhiteSpace(candidate.ProviderItemId))
        {
            var guessedBridgeId = GuessBridgeIdKey(candidate.ProviderName, mediaType, targetFieldGroup);
            if (!string.IsNullOrWhiteSpace(guessedBridgeId))
                fields.TryAdd(guessedBridgeId, candidate.ProviderItemId!);
        }

        return fields;
    }

    private static Dictionary<string, string> BuildUniverseFieldBag(Domain.Models.UniverseCandidate candidate, string mediaType, string targetFieldGroup)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(candidate.Label))
            fields[MetadataFieldConstants.Title] = candidate.Label;
        if (!string.IsNullOrWhiteSpace(candidate.Author))
        {
            fields[MetadataFieldConstants.Author] = candidate.Author;
            fields.TryAdd(MetadataFieldConstants.Artist, candidate.Author);
        }
        if (!string.IsNullOrWhiteSpace(candidate.Director))
            fields[MetadataFieldConstants.Director] = candidate.Director;
        if (!string.IsNullOrWhiteSpace(candidate.Description))
            fields[MetadataFieldConstants.Description] = candidate.Description;
        if (!string.IsNullOrWhiteSpace(candidate.Year))
            fields[MetadataFieldConstants.Year] = candidate.Year;
        if (!string.IsNullOrWhiteSpace(candidate.CoverUrl))
            fields[MetadataFieldConstants.CoverUrl] = candidate.CoverUrl;

        foreach (var (key, value) in candidate.MediaTypeMetadata ?? new Dictionary<string, string>())
        {
            if (!string.IsNullOrWhiteSpace(value))
                fields[key] = value;
        }

        switch (targetFieldGroup)
        {
            case "album":
                fields[MetadataFieldConstants.Album] = candidate.Label;
                break;
            case "artist":
                fields[MetadataFieldConstants.Artist] = candidate.Label;
                break;
            case "narrator":
                fields[MetadataFieldConstants.Narrator] = candidate.Label;
                break;
            case "series":
                fields[MetadataFieldConstants.Series] = candidate.Label;
                break;
            case "show":
                fields[MetadataFieldConstants.ShowName] = candidate.Label;
                break;
            case "show_episode":
                fields.TryAdd(MetadataFieldConstants.EpisodeTitle, candidate.Label);
                break;
            case "movie_identity":
            case "book_identity":
            case "audiobook_identity":
            case "issue":
                fields[MetadataFieldConstants.Title] = candidate.Label;
                break;
        }

        if (string.Equals(mediaType, MediaType.Music.ToString(), StringComparison.OrdinalIgnoreCase)
            && fields.TryGetValue(MetadataFieldConstants.Author, out var creator))
        {
            fields.TryAdd(MetadataFieldConstants.Artist, creator);
        }

        return fields;
    }

    private static Dictionary<string, string> ExtractFields(
        IReadOnlyDictionary<string, string> source,
        IEnumerable<string> keys,
        bool allowContainerTitleAliases = true)
    {
        var output = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            if (TryResolveFieldValue(source, key, allowContainerTitleAliases, out var value))
                output[key] = value;
        }

        return output;
    }

    private static bool TryResolveFieldValue(
        IReadOnlyDictionary<string, string> source,
        string key,
        bool allowContainerTitleAliases,
        out string value)
    {
        if (source.TryGetValue(key, out value!) && !string.IsNullOrWhiteSpace(value))
            return true;

        var aliases = key switch
        {
            MetadataFieldConstants.Artist => [MetadataFieldConstants.Author],
            MetadataFieldConstants.Author => [MetadataFieldConstants.Artist],
            MetadataFieldConstants.Album when allowContainerTitleAliases => [MetadataFieldConstants.Title],
            MetadataFieldConstants.Series when allowContainerTitleAliases => [MetadataFieldConstants.Title],
            MetadataFieldConstants.ShowName when allowContainerTitleAliases => [MetadataFieldConstants.Title],
            _ => Array.Empty<string>(),
        };

        foreach (var alias in aliases)
        {
            if (source.TryGetValue(alias, out value!) && !string.IsNullOrWhiteSpace(value))
                return true;
        }

        value = string.Empty;
        return false;
    }

    private static string? GuessBridgeIdKey(string providerName, string mediaType, string targetFieldGroup)
    {
        var normalized = providerName?.Trim().ToLowerInvariant() ?? "";
        if (normalized.Contains("comic"))
            return BridgeIdKeys.ComicVineId;
        if (normalized.Contains("tmdb"))
            return string.Equals(mediaType, MediaType.TV.ToString(), StringComparison.OrdinalIgnoreCase)
                   && string.Equals(targetFieldGroup, "show_episode", StringComparison.OrdinalIgnoreCase)
                ? BridgeIdKeys.TmdbEpisodeId
                : BridgeIdKeys.TmdbId;
        if (normalized.Contains("imdb"))
            return BridgeIdKeys.ImdbId;
        if (normalized.Contains("audible"))
            return BridgeIdKeys.AudibleId;
        if (normalized.Contains("apple_books"))
            return BridgeIdKeys.AppleBooksId;
        if (normalized.Contains("open_library"))
            return BridgeIdKeys.OpenLibraryId;
        if (normalized.Contains("apple_music"))
        {
            return targetFieldGroup switch
            {
                "artist" => BridgeIdKeys.AppleArtistId,
                "album" => BridgeIdKeys.AppleMusicCollectionId,
                _ => BridgeIdKeys.AppleMusicId,
            };
        }

        return mediaType switch
        {
            "Music" when targetFieldGroup == "album" => BridgeIdKeys.AppleMusicCollectionId,
            "Music" when targetFieldGroup == "artist" => BridgeIdKeys.AppleArtistId,
            _ => null,
        };
    }

    private static async Task<IReadOnlyList<string>> ClearStaleIdsAsync(
        Guid assetId,
        WorkLineage? lineage,
        CanonicalTargetPolicy policy,
        ItemCanonicalApplyRequest request,
        IItemCanonicalDataService itemCanonicalData,
        CancellationToken ct)
    {
        var retainedIdKeys = request.BridgeIds.Keys
            .Concat(request.QidFields.Keys)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var groupIdKeys = policy.BridgeIdKeys.Concat(policy.QidFieldKeys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var toClear = groupIdKeys.Where(key => !retainedIdKeys.Contains(key)).ToList();

        if (toClear.Count > 0)
        {
            var artifacts = toClear
                .Select(key => new ItemCanonicalIdentityArtifact(
                    ResolveScopedTarget(assetId, lineage, key),
                    key))
                .ToList();
            await itemCanonicalData.DeleteIdentityArtifactsAsync(artifacts, ct);
        }

        return toClear;
    }

    private static async Task<string?> FindChildParentIdentityConflictAsync(
        CanonicalTargetPolicy policy,
        WorkLineage lineage,
        IReadOnlyDictionary<string, string> selectedFields,
        IReadOnlyDictionary<string, string> selectedBridgeIds,
        ICanonicalValueRepository canonicalRepo,
        IBridgeIdRepository bridgeIdRepo,
        CancellationToken ct)
    {
        if (lineage.TargetForSelfScope == lineage.TargetForParentScope)
            return null;

        string? parentField = policy.TargetFieldGroup switch
        {
            "show_episode" => MetadataFieldConstants.ShowName,
            "track" => MetadataFieldConstants.Album,
            _ => null,
        };
        if (string.IsNullOrWhiteSpace(parentField))
            return null;

        var parentCanonicals = await canonicalRepo.GetByEntityAsync(lineage.TargetForParentScope, ct);
        var currentParentName = parentCanonicals
            .FirstOrDefault(value => string.Equals(value.Key, parentField, StringComparison.OrdinalIgnoreCase))?.Value
            ?? parentCanonicals.FirstOrDefault(value =>
                string.Equals(value.Key, MetadataFieldConstants.Title, StringComparison.OrdinalIgnoreCase))?.Value;

        if (selectedFields.TryGetValue(parentField, out var selectedParentName)
            && !string.IsNullOrWhiteSpace(currentParentName)
            && !IdentityTextEquals(currentParentName, selectedParentName))
        {
            var childLabel = policy.TargetFieldGroup == "show_episode" ? "episode" : "track";
            var parentLabel = policy.TargetFieldGroup == "show_episode" ? "series" : "album";
            return $"This {childLabel} match belongs to '{selectedParentName}', not the current {parentLabel} '{currentParentName}'. Move the {childLabel} from the Details panel before applying this identity match.";
        }

        if (policy.TargetFieldGroup == "show_episode"
            && selectedBridgeIds.TryGetValue(BridgeIdKeys.TmdbId, out var selectedShowId))
        {
            var existingShowId = await bridgeIdRepo.FindAsync(lineage.TargetForParentScope, BridgeIdKeys.TmdbId, ct);
            if (existingShowId is not null
                && !string.Equals(existingShowId.IdValue, selectedShowId, StringComparison.OrdinalIgnoreCase))
            {
                return "This episode match resolves to a different TMDB series. Move the episode from the Details panel before applying the episode identity.";
            }
        }

        return null;
    }

    private static bool IdentityTextEquals(string left, string right)
    {
        static string Normalize(string value) =>
            new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

        return string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);
    }

    private static Guid ResolvePolicyIdentityTarget(
        Guid assetId,
        WorkLineage lineage,
        CanonicalTargetPolicy policy) =>
        IsContainerIdentityPolicy(policy)
            ? lineage.TargetForParentScope
            : assetId;

    private static Guid ResolvePolicyScopedTarget(
        Guid assetId,
        WorkLineage lineage,
        CanonicalTargetPolicy policy,
        string key) =>
        IsContainerIdentityPolicy(policy) || ClaimScopeCatalog.IsParentScoped(key, lineage.MediaType)
            ? lineage.TargetForParentScope
            : assetId;

    private static Guid ResolvePolicyWorkTarget(
        WorkLineage lineage,
        CanonicalTargetPolicy policy,
        string key) =>
        IsContainerIdentityPolicy(policy) || ClaimScopeCatalog.IsParentScoped(key, lineage.MediaType)
            ? lineage.TargetForParentScope
            : lineage.TargetForSelfScope;

    private static bool IsContainerIdentityPolicy(CanonicalTargetPolicy policy) =>
        string.Equals(policy.TargetKind, "container", StringComparison.OrdinalIgnoreCase);

    private static bool SupportsImmediateRetailArtworkReplacement(CanonicalTargetPolicy policy) =>
        policy.TargetFieldGroup is
            "book_identity" or
            "audiobook_identity" or
            "issue" or
            "movie_identity" or
            "album" or
            "show";

    private static async Task ReplaceScopedExternalIdentifiersAsync(
        WorkLineage lineage,
        CanonicalTargetPolicy policy,
        IReadOnlyCollection<string> staleKeys,
        IReadOnlyDictionary<string, string> replacements,
        IItemCanonicalDataService itemCanonicalData,
        CancellationToken ct)
    {
        var targetWorkIds = policy.BridgeIdKeys
            .Concat(replacements.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .GroupBy(key => ResolvePolicyWorkTarget(lineage, policy, key));

        foreach (var target in targetWorkIds)
        {
            var keysForTarget = staleKeys
                .Where(key => ResolvePolicyWorkTarget(lineage, policy, key) == target.Key)
                .ToList();
            var replacementsForTarget = replacements
                .Where(pair => ResolvePolicyWorkTarget(lineage, policy, pair.Key) == target.Key)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            await itemCanonicalData.ReplaceExternalIdentifiersAsync(
                target.Key,
                keysForTarget,
                replacementsForTarget,
                ct);
        }
    }

    private static Guid ResolveScopedTarget(Guid assetId, WorkLineage? lineage, string key)
    {
        if (lineage is null)
            return assetId;

        return ClaimScopeCatalog.IsParentScoped(key, lineage.MediaType)
            ? lineage.TargetForParentScope
            : assetId;
    }

    private static string NormalizeLinkState(string linkState) => linkState.Trim().ToLowerInvariant() switch
    {
        "linked" => "linked",
        "provider_only" => "provider_only",
        _ => "text_only",
    };

    private static string NormalizeSearchMode(string? searchMode) =>
        (searchMode ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "wikidata_only" => "wikidata_only",
            "combined" => "combined",
            _ => "retail_only",
        };

    private static string GetLinkStatusLabel(string linkState) => NormalizeLinkState(linkState) switch
    {
        "linked" => "Linked to Wikidata",
        "provider_only" => "Linked to provider only",
        _ => "Saved without external link",
    };

}
