using System.Text.Json.Nodes;
using System.Text.Json;
using System.Globalization;
using System.Text.Json.Serialization;
using MediaEngine.Api.Http;
using MediaEngine.Api.Models;
using MediaEngine.Api.Security;
using MediaEngine.Api.Services;
using MediaEngine.Api.Services.Metadata;
using MediaEngine.Api.Services.ReadServices;
using MediaEngine.Contracts.Metadata;
using MediaEngine.Domain;
using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Helpers;
using MediaEngine.Providers.Models;
using MediaEngine.Storage.Contracts;
using ArtworkResolutionContext = MediaEngine.Api.Services.MetadataArtworkResolutionContext;
using EditorLaunchContext = MediaEngine.Api.Services.MetadataEditorLaunchContext;

namespace MediaEngine.Api.Endpoints;

public static partial class MetadataEndpoints
{

    /// <summary>
    /// Fields that may be set via user-locked claims. Structured metadata
    /// (title, author, year, genre, series, description, etc.) must come from
    /// the provider hierarchy only. Users may only contribute personal ratings,
    /// media-type corrections, and custom collection tags.
    /// </summary>
    private static readonly HashSet<string> UserLockableFields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            MetadataFieldConstants.Rating,          // User's personal rating for this title
            MetadataFieldConstants.MediaTypeField,  // User correction of detected media type (see /reclassify)
            MetadataFieldConstants.CustomTags,      // User-defined collection / labelling tags
        };

    public static IEndpointRouteBuilder MapMetadataEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/metadata")
                       .WithTags("Metadata");

        // -- GET /metadata/claims/{entityId} ----------------------------------
        group.MapGet("/claims/{entityId:guid}", async (
            Guid entityId,
            IMetadataClaimHistoryReadService claimHistoryReadService,
            CancellationToken ct) =>
        {
            var claims = await claimHistoryReadService.GetClaimHistoryAsync(entityId, ct);
            return Results.Ok(claims);
        })
        .WithName("GetClaimHistory")
        .WithSummary("Returns all metadata claims for a Work or Edition, ordered by claimed_at.")
        .Produces<List<ClaimDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        // -- GET /metadata/conflicts -----------------------------------------
        group.MapGet("/conflicts", async (
            ICanonicalValueRepository canonicalRepo,
            CancellationToken ct) =>
        {
            var conflicted = await canonicalRepo.GetConflictedAsync(ct);
            var dtos = conflicted.Select(ConflictDto.FromDomain).ToList();
            return Results.Ok(dtos);
        })
        .WithName("GetConflicts")
        .WithSummary("Returns all canonical values with unresolved metadata conflicts.")
        .Produces<List<ConflictDto>>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // -- PATCH /metadata/lock-claim ---------------------------------------
        group.MapMethods("/lock-claim", ["PATCH"], async (
            LockClaimRequest request,
            IMetadataClaimRepository claimRepo,
            ICanonicalValueRepository canonicalRepo,
            ITransactionJournal journal,
            IEventPublisher publisher,
            CancellationToken ct) =>
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(request.ClaimKey))
                return ApiErrors.BadRequest("claim_key must not be empty.");
            if (string.IsNullOrWhiteSpace(request.ChosenValue))
                return ApiErrors.BadRequest("chosen_value must not be empty.");
            if (!UserLockableFields.Contains(request.ClaimKey))
                return ApiErrors.BadRequest(
                    $"Field '{request.ClaimKey}' cannot be user-locked. " +
                    $"Only these fields accept user locks: {string.Join(", ", UserLockableFields)}. " +
                    "Structured metadata (title, author, year, etc.) is resolved by the provider hierarchy.");

            var lockedAt = DateTimeOffset.UtcNow;

            // 1. Insert a user-locked claim (confidence 1.0).
            var claim = new MetadataClaim
            {
                Id           = Guid.NewGuid(),
                EntityId     = request.EntityId,
                ProviderId   = WellKnownProviders.UserManual,
                ClaimKey     = request.ClaimKey,
                ClaimValue   = request.ChosenValue,
                ClaimedAt    = lockedAt,
                IsUserLocked = true,
            };
            await claimRepo.InsertBatchAsync([claim], ct);

            // 2. Upsert the canonical value so the Dashboard sees the change immediately.
            //    User-locked claims resolve any conflict, so is_conflicted is set to 0.
            await canonicalRepo.UpsertBatchAsync(
                [new CanonicalValue
                {
                    EntityId = request.EntityId,
                    Key = request.ClaimKey,
                    Value = request.ChosenValue,
                    LastScoredAt = lockedAt,
                    IsConflicted = false,
                }],
                ct);

            // 3. Audit trail.
            journal.Log("CLAIM_USER_LOCKED", "MetadataClaim", request.EntityId);

            // 4. Broadcast so the Dashboard refreshes.
            await publisher.PublishAsync(SignalREvents.MetadataHarvested, new
            {
                entity_id     = request.EntityId,
                provider_name = "user_manual",
                updated_fields = new[] { request.ClaimKey },
            });

            return Results.Ok(new LockClaimResponse
            {
                EntityId    = request.EntityId,
                ClaimKey    = request.ClaimKey,
                ChosenValue = request.ChosenValue,
                LockedAt    = lockedAt,
            });
        })
        .WithName("LockClaim")
        .WithSummary("Create a user-locked metadata claim and update the canonical value. Used by the Curator's Drawer.")
        .Produces<LockClaimResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAdminOrCurator();

        // -- POST /metadata/hydrate/{entityId} -----------------------------
        group.MapPost("/hydrate/{entityId:guid}", async (
            Guid entityId,
            ICanonicalValueRepository canonicalRepo,
            IHydrationPipelineService pipeline,
            IDeferredEnrichmentRepository deferredRepo,
            CancellationToken ct) =>
        {
            // Load existing canonical values to build lookup hints.
            var canonicals = await canonicalRepo.GetByEntityAsync(entityId, ct);
            var hints = canonicals.ToDictionary(
                c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase);

            // Run the three-stage hydration pipeline synchronously (full Universe pass).
            Domain.Models.HydrationResult result;
            try
            {
                result = await pipeline.RunSynchronousAsync(new Domain.Models.HarvestRequest
                {
                    EntityId   = entityId,
                    EntityType = EntityType.MediaAsset,
                    MediaType  = Domain.Enums.MediaType.Unknown,
                    Hints      = hints,
                    Pass       = Domain.Enums.HydrationPass.Universe,
                }, ct);
            }
            catch (Exception ex)
            {
                return Results.Ok(new HydrateResponse
                {
                    Success = false,
                    Message = $"Hydration pipeline failed: {ex.Message}",
                });
            }

            // Mark any pending Pass 2 deferred enrichment for this entity as processed.
            // User-triggered hydrate runs the full Universe pipeline, so Pass 2 is satisfied.
            await deferredRepo.MarkProcessedByEntityAsync(entityId, ct);

            return Results.Ok(new HydrateResponse
            {
                WikidataQid  = result.WikidataQid,
                ClaimsAdded  = result.TotalClaimsAdded,
                Stage1Claims = result.Stage1ClaimsAdded,
                Stage2Claims = result.Stage2ClaimsAdded,
                NeedsReview  = result.NeedsReview,
                ReviewItemId = result.ReviewItemId,
                Success      = true,
                Message      = $"Hydrated {result.TotalClaimsAdded} claims across 2 stages"
                             + (result.WikidataQid is not null ? $" (QID: {result.WikidataQid})" : "")
                             + (result.NeedsReview ? " — needs review." : "."),
            });
        })
        .WithName("HydrateEntity")
        .WithSummary("Run the two-stage hydration pipeline for a Work or Edition entity. Admin or Curator.")
        .Produces<HydrateResponse>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // -- POST /metadata/search -----------------------------------------
        group.MapPost("/search", async (
            MetadataSearchRequest request,
            IEnumerable<IExternalMetadataProvider> providers,
            IConfigurationLoader configLoader,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var searchLogger = loggerFactory.CreateLogger("MetadataSearch");

            if (string.IsNullOrWhiteSpace(request.ProviderName))
                return ApiErrors.BadRequest("provider_name is required.");

            if (string.IsNullOrWhiteSpace(request.Query))
                return ApiErrors.BadRequest("query is required.");

            // Find the named provider.
            var provider = providers.FirstOrDefault(
                p => string.Equals(p.Name, request.ProviderName, StringComparison.OrdinalIgnoreCase));

            if (provider is null)
                return ApiErrors.NotFound($"Provider '{request.ProviderName}' not found.");

            // Parse media type.
            var mediaType = Domain.Enums.MediaType.Unknown;
            if (!string.IsNullOrEmpty(request.MediaType))
                Enum.TryParse(request.MediaType, ignoreCase: true, out mediaType);

            searchLogger.LogInformation(
                "Search: provider={Provider}, mediaType={MediaType}, query={Query}",
                request.ProviderName, mediaType, request.Query);

            // Resolve base URL from provider config.
            var providerConfig = configLoader.LoadProvider(request.ProviderName);
            var baseUrl = providerConfig?.Endpoints.Values.FirstOrDefault() ?? string.Empty;

            // Build the lookup request using the search query as the title hint.
            var lookupRequest = new ProviderLookupRequest
            {
                EntityId   = Guid.Empty,
                EntityType = EntityType.MediaAsset,
                MediaType  = mediaType,
                Title      = request.Query,
                BaseUrl    = baseUrl,
            };

            var limit = Math.Clamp(request.Limit, 1, 50);
            var results = await provider.SearchAsync(lookupRequest, limit, ct);

            searchLogger.LogInformation(
                "Search complete: provider={Provider}, results={Count}",
                request.ProviderName, results.Count);

            var response = new MetadataSearchResponse
            {
                ProviderName = request.ProviderName,
                Query        = request.Query,
                Results = results.Select(r => new SearchResultResponse
                {
                    Title          = r.Title,
                    Author         = r.Author,
                    Description    = r.Description,
                    Year           = r.Year,
                    ThumbnailUrl   = r.ThumbnailUrl,
                    ProviderItemId = r.ProviderItemId,
                    Confidence     = r.Confidence,
                }).ToList(),
            };

            return Results.Ok(response);
        })
        .WithName("SearchMetadata")
        .WithSummary("Search an external metadata provider for multiple result candidates. Admin or Curator.")
        .Produces<MetadataSearchResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        // -- PUT /metadata/{entityId}/override ----------------------------
        group.MapPut("/{entityId:guid}/override", async (
            Guid entityId,
            MetadataOverrideRequest request,
            IMetadataClaimRepository claimRepo,
            ICanonicalValueRepository canonicalRepo,
            ISystemActivityRepository activityRepo,
            IWriteBackService writeBack,
            IEventPublisher publisher,
            CancellationToken ct) =>
        {
            if (request.Fields.Count == 0)
                return ApiErrors.BadRequest("At least one field override is required.");

            var now = DateTimeOffset.UtcNow;
            var updatedKeys = new List<string>();

            var claims = new List<MetadataClaim>();
            var canonicals = new List<CanonicalValue>();

            // Validate all keys before persisting anything.
            var rejectedKeys = request.Fields.Keys
                .Where(k => !string.IsNullOrWhiteSpace(k) && !UserLockableFields.Contains(k))
                .ToList();
            if (rejectedKeys.Count > 0)
                return ApiErrors.BadRequest(
                    $"Fields cannot be user-locked: {string.Join(", ", rejectedKeys)}. " +
                    $"Only these fields accept user locks: {string.Join(", ", UserLockableFields)}. " +
                    "Structured metadata (title, author, year, etc.) is resolved by the provider hierarchy.");

            foreach (var (key, value) in request.Fields)
            {
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                    continue;

                // 1. Create a user-locked claim (confidence 1.0, never overridden).
                claims.Add(new MetadataClaim
                {
                    Id           = Guid.NewGuid(),
                    EntityId     = entityId,
                    ProviderId   = WellKnownProviders.UserManual,
                    ClaimKey     = key,
                    ClaimValue   = value,
                    ClaimedAt    = now,
                    IsUserLocked = true,
                });

                // 2. Prepare canonical value upsert.
                canonicals.Add(new CanonicalValue
                {
                    EntityId     = entityId,
                    Key          = key,
                    Value        = value,
                    LastScoredAt = now,
                });

                updatedKeys.Add(key);
            }

            // Persist all claims and canonical values in batch.
            if (claims.Count > 0)
                await claimRepo.InsertBatchAsync(claims, ct);
            if (canonicals.Count > 0)
                await canonicalRepo.UpsertBatchAsync(canonicals, ct);

            if (updatedKeys.Count == 0)
                return ApiErrors.BadRequest("No valid field overrides provided.");

            // 3. Log to activity ledger.
            await activityRepo.LogAsync(new SystemActivityEntry
            {
                ActionType = SystemActionType.MetadataManualOverride,
                EntityId   = entityId,
                Detail     = $"Manual override: {updatedKeys.Count} field(s) ? {string.Join(", ", updatedKeys)}.",
            }, ct);

            // 4. Broadcast so the Dashboard refreshes.
            await publisher.PublishAsync(SignalREvents.MetadataHarvested, new
            {
                entity_id      = entityId,
                provider_name  = "user_manual",
                updated_fields = updatedKeys.ToArray(),
            }, ct);

            // 5. Write-back: write manual overrides to the physical file.
            try
            {
                await writeBack.WriteMetadataAsync(entityId, "manual_override", ct);
            }
            catch
            {
                // Non-fatal ? write-back failure should not prevent override success.
            }

            return Results.Ok(new MetadataOverrideResponse
            {
                EntityId       = entityId,
                FieldsUpdated  = updatedKeys.Count,
                OverriddenAt   = now,
            });
        })
        .WithName("OverrideMetadata")
        .WithSummary("Manually override metadata fields for an entity. Creates user-locked claims at confidence 1.0.")
        .Produces<MetadataOverrideResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAdminOrCurator();

        // -- POST /metadata/{entityId}/reclassify ------------------------------
        group.MapPost("/{entityId:guid}/reclassify", async (
            Guid entityId,
            ReclassifyRequest request,
            IMetadataClaimRepository claimRepo,
            ICanonicalValueRepository canonicalRepo,
            IReviewQueueRepository reviewRepo,
            IHydrationPipelineService pipeline,
            IEventPublisher publisher,
            ISystemActivityRepository activityRepo,
            IMetadataEndpointDataService metadataData,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.MediaType))
                return ApiErrors.BadRequest("media_type is required.");

            // Validate the media type string parses to a known MediaType enum value.
            if (!Enum.TryParse<Domain.Enums.MediaType>(request.MediaType, ignoreCase: true, out var newMediaType)
                || newMediaType == Domain.Enums.MediaType.Unknown)
            {
                return ApiErrors.BadRequest($"Invalid media type: {request.MediaType}");
            }

            var now = DateTimeOffset.UtcNow;
            var requestedEntityId = entityId;
            var reclassifyTarget = await metadataData.ResolveReclassifyTargetAsync(entityId, ct);
            var targetAssetId = reclassifyTarget.TargetAssetId;
            var targetWorkId = reclassifyTarget.WorkId;

            // 1. Create user-locked media_type claims at confidence 1.0.
            var claimEntityIds = new[] { (Guid?)targetAssetId, targetWorkId, requestedEntityId }
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct()
                .ToList();

            var claims = claimEntityIds.Select(claimEntityId => new MetadataClaim
            {
                Id           = Guid.NewGuid(),
                EntityId     = claimEntityId,
                ProviderId   = WellKnownProviders.UserManual,
                ClaimKey     = MetadataFieldConstants.MediaTypeField,
                ClaimValue   = newMediaType.ToString(),
                ClaimedAt    = now,
                IsUserLocked = true,
            }).ToList();
            await claimRepo.InsertBatchAsync(claims, ct);

            // 2. Upsert canonical media_type values and the work discriminator used by detail/search screens.
            await canonicalRepo.UpsertBatchAsync(claimEntityIds.Select(claimEntityId => new CanonicalValue
                {
                    EntityId     = claimEntityId,
                    Key          = MetadataFieldConstants.MediaTypeField,
                    Value        = newMediaType.ToString(),
                    LastScoredAt = now,
                    IsConflicted = false,
                })
                .ToList(), ct);

            if (targetWorkId is { } workId)
            {
                await metadataData.UpdateWorkMediaTypeAsync(workId, newMediaType.ToString(), ct);
            }

            // 3. Resolve any pending AmbiguousMediaType review items for this entity.
            bool reviewResolved = false;
            foreach (var reviewEntityId in claimEntityIds)
            {
                var reviews = await reviewRepo.GetByEntityAsync(reviewEntityId, ct);
                foreach (var review in reviews.Where(r =>
                    r.Status == ReviewStatus.Pending &&
                    r.Trigger == ReviewTrigger.AmbiguousMediaType))
                {
                    await reviewRepo.UpdateStatusAsync(review.Id, ReviewStatus.Resolved, "user", ct);
                    reviewResolved = true;
                }
            }

            // 4. Re-trigger hydration with the correct media type.
            var canonicals = await canonicalRepo.GetByEntityAsync(targetAssetId, ct);
            var hints = canonicals.ToDictionary(
                c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase);

            await pipeline.EnqueueAsync(new HarvestRequest
            {
                EntityId   = targetAssetId,
                EntityType = EntityType.MediaAsset,
                MediaType  = newMediaType,
                Hints      = hints,
            }, ct);

            // 5. Log activity.
            await activityRepo.LogAsync(new SystemActivityEntry
            {
                ActionType = SystemActionType.MetadataRefreshed,
                EntityId   = requestedEntityId,
                EntityType = "MediaAsset",
                Detail     = $"Media type reclassified to {newMediaType} by user.",
            }, ct);

            // 6. Broadcast events.
            await publisher.PublishAsync(SignalREvents.MetadataHarvested, new
            {
                entity_id  = requestedEntityId,
                media_type = newMediaType.ToString(),
            }, ct);

            if (reviewResolved)
            {
                await publisher.PublishAsync(SignalREvents.ReviewItemResolved, new
                {
                    entity_id = requestedEntityId,
                    status    = "Resolved",
                }, ct);
            }

            return Results.Ok(new ReclassifyResponse
            {
                EntityId        = requestedEntityId,
                NewMediaType    = newMediaType.ToString(),
                ReclassifiedAt  = now,
                ReviewResolved  = reviewResolved,
            });
        })
        .WithName("ReclassifyMediaType")
        .WithSummary("Reclassify a media asset to a different media type. Creates a user-locked claim and re-triggers hydration.")
        .Produces<ReclassifyResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAdminOrCurator();

        // -- GET /metadata/{entityId}/editor-context -------------------------
        group.MapGet("/{entityId:guid}/editor-context", async (
            Guid entityId,
            ICanonicalValueRepository canonicalRepo,
            ILibraryItemRepository libraryItemRepo,
            IMetadataEndpointDataService metadataData,
            CancellationToken ct) =>
        {
            var context = await ResolveEditorScopeContextAsync(entityId, canonicalRepo, libraryItemRepo, metadataData, ct);
            if (context is null)
                return ApiErrors.NotFound($"Editor context for {entityId} not found.");

            return Results.Ok(new MediaEditorContextEnvelope(
                context.LaunchEntityId,
                context.LaunchEntityKind,
                context.MediaType,
                context.EditorMode,
                context.AvailableTabs,
                context.ContentTabLabel,
                context.SupportsFileTab,
                context.FileMetadataSyncStatus,
                context.CurrentTargetSummary,
                context.IdentitySummary,
                context.FieldLockMap,
                context.DisplayOverrideKeys,
                context.DisplayOverrides,
                context.InitialScope,
                context.Scopes.Select(scope => new MediaEditorScopeEnvelope(
                    scope.ScopeId,
                    scope.Label,
                    scope.Order,
                    scope.FieldEntityId,
                    scope.FieldEntityKind,
                    scope.ArtworkOwnerEntityId,
                    scope.ArtworkOwnerEntityKind,
                    scope.DisplayTitle,
                    scope.DisplaySubtitle,
                    scope.BreadcrumbLabel,
                    scope.CanonicalTargetGroup,
                    context.ScopeIdentitySummaries.TryGetValue(scope.FieldEntityId, out var scopeIdentity)
                        ? scopeIdentity
                        : context.IdentitySummary,
                    scope.ScopeSummary,
                    scope.ReadOnlyHint,
                    scope.CanEditFields,
                    scope.CanEditArtwork))
                    .ToList()));
        })
        .WithName("GetMediaEditorContext")
        .WithSummary("Resolve scope-aware edit panel context for a launch entity.")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        MapMediaEditorNavigatorEndpoints(group);

        // -- GET /metadata/{entityId}/artwork/{scopeId} ---------------------
        group.MapGet("/{entityId:guid}/artwork/{scopeId}", async (
            Guid entityId,
            string scopeId,
            ICanonicalValueRepository canonicalRepo,
            ILibraryItemRepository libraryItemRepo,
            IMetadataEndpointDataService metadataData,
            ArtworkScopeService artworkScopeService,
            CancellationToken ct) =>
        {
            var context = await ResolveEditorScopeContextAsync(entityId, canonicalRepo, libraryItemRepo, metadataData, ct);
            if (context is null)
                return ApiErrors.NotFound($"Editor context for {entityId} not found.");

            var scope = context.Scopes.FirstOrDefault(candidate =>
                string.Equals(candidate.ScopeId, scopeId, StringComparison.OrdinalIgnoreCase));
            if (scope is null)
                return ApiErrors.NotFound($"Scope '{scopeId}' was not found for {entityId}.");

            var artwork = await artworkScopeService.BuildScopedArtworkEnvelopeAsync(scope, ct);
            return Results.Ok(artwork);
        })
        .WithName("GetScopedArtworkEditor")
        .WithSummary("Return grouped artwork variants for one editor scope.")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        // -- POST /metadata/{entityId}/artwork/{scopeId}/refresh-provider ---
        group.MapPost("/{entityId:guid}/artwork/{scopeId}/refresh-provider", async (
            Guid entityId,
            string scopeId,
            ICanonicalValueRepository canonicalRepo,
            ILibraryItemRepository libraryItemRepo,
            IImageEnrichmentService imageEnrichment,
            IMetadataEndpointDataService metadataData,
            ArtworkScopeService artworkScopeService,
            CancellationToken ct) =>
        {
            var context = await ResolveEditorScopeContextAsync(entityId, canonicalRepo, libraryItemRepo, metadataData, ct);
            if (context is null)
                return ApiErrors.NotFound($"Editor context for {entityId} not found.");

            var scope = context.Scopes.FirstOrDefault(candidate =>
                string.Equals(candidate.ScopeId, scopeId, StringComparison.OrdinalIgnoreCase));
            if (scope is null)
                return ApiErrors.NotFound($"Scope '{scopeId}' was not found for {entityId}.");

            if (!ArtworkScopeService.IsProviderArtworkRefreshSupported(scope))
            {
                return Results.Ok(ArtworkScopeService.CreateProviderArtworkRefreshEnvelope(
                    status: "Skipped",
                    skippedReason: "unsupported_scope",
                    message: "Provider artwork refresh is available for movie and TV artwork scopes.",
                    mediaType: scope.MediaType));
            }

            var target = await artworkScopeService.ResolveProviderArtworkRefreshTargetAsync(scope, ct);
            if (target.Skipped is not null)
                return Results.Ok(target.Skipped);

            var result = await imageEnrichment.EnrichWorkImagesAsync(
                target.RepresentativeAssetId!.Value,
                target.WorkQid!,
                ct);

            return Results.Ok(ArtworkScopeService.MapProviderArtworkRefreshResult(result));
        })
        .WithName("RefreshScopedProviderArtwork")
        .WithSummary("Refresh provider artwork for one editor scope without rerunning full identity matching.")
        .Produces<ProviderArtworkRefreshEnvelope>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        // -- GET /metadata/{entityId}/artwork --------------------------------
        group.MapGet("/{entityId:guid}/artwork", async (
            Guid entityId,
            IMediaAssetRepository assetRepo,
            IWorkRepository workRepo,
            IEntityAssetRepository entityAssetRepo,
            ICanonicalValueRepository canonicalRepo,
            ILibraryItemRepository libraryItemRepo,
            IMetadataEndpointDataService metadataData,
            CancellationToken ct) =>
        {
            var context = await metadataData.ResolveArtworkContextAsync(entityId, ct);
            var detail = context.WorkId is Guid workId
                ? await libraryItemRepo.GetDetailAsync(workId, ct)
                : null;

            var assets = new List<EntityAsset>();
            foreach (var artworkEntityId in context.ArtworkEntityIds)
            {
                assets.AddRange(await entityAssetRepo.GetByEntityAsync(artworkEntityId.ToString(), null, ct));
            }

            var canonicalSources = new List<Guid>();
            ArtworkScopeService.AddCanonicalSource(canonicalSources, context.WorkId);
            ArtworkScopeService.AddCanonicalSource(canonicalSources, context.RootWorkId);
            ArtworkScopeService.AddCanonicalSource(canonicalSources, context.PrimaryAssetId);
            ArtworkScopeService.AddCanonicalSource(canonicalSources, context.RootPrimaryAssetId);

            var canonicals = new List<CanonicalValue>();
            foreach (var sourceId in canonicalSources)
            {
                canonicals.AddRange(await canonicalRepo.GetByEntityAsync(sourceId, ct));
            }

            var slots = new[]
            {
                "CoverArt",
                "SquareArt",
                "Background",
                "Banner",
                "Logo",
            };

            var payload = slots.Select(assetType =>
            {
                var variants = assets
                    .Where(asset => string.Equals(asset.AssetTypeValue, assetType, StringComparison.OrdinalIgnoreCase))
                    .GroupBy(ArtworkScopeService.BuildArtworkVariantIdentity, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group
                        .OrderByDescending(asset => asset.IsPreferred)
                        .ThenByDescending(asset => asset.CreatedAt)
                        .First())
                    .OrderByDescending(asset => asset.IsPreferred)
                    .ThenByDescending(asset => asset.CreatedAt)
                    .Select(ArtworkScopeService.MapArtworkVariant)
                    .ToList();

                var preferredUrl = ArtworkScopeService.GetArtworkCanonicalValue(canonicals, assetType)
                                   ?? ArtworkScopeService.GetArtworkDetailUrl(detail, assetType);

                if (!string.IsNullOrWhiteSpace(preferredUrl)
                    && !variants.Any(variant => string.Equals(variant.ImageUrl, preferredUrl, StringComparison.OrdinalIgnoreCase)))
                {
                    variants.Insert(0, new ArtworkVariantEnvelope(
                        Guid.Empty,
                        assetType,
                        preferredUrl,
                        true,
                        ArtworkScopeService.InferSyntheticArtworkOrigin(canonicals, assetType, detail?.ArtworkSource),
                        ProviderName: null,
                        CanDelete: false,
                        CreatedAt: null));
                }

                return new ArtworkSlotEnvelope(assetType, variants);
            }).ToList();

            return Results.Ok(new ArtworkEditorEnvelope(entityId, payload));
        })
        .WithName("GetArtworkEditor")
        .WithSummary("Return grouped artwork variants for the editor.")
        .Produces(StatusCodes.Status200OK)
        .RequireAnyRole();

        // -- POST /metadata/{entityId}/cover ---------------------------------
        group.MapPost("/{entityId:guid}/cover", async (
            Guid entityId,
            IEntityAssetRepository entityAssetRepo,
            IAssetExportService assetExportService,
            ISystemActivityRepository activityRepo,
            IMetadataEndpointDataService metadataData,
            ArtworkScopeService artworkScopeService,
            HttpRequest httpRequest,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var coverLogger = loggerFactory.CreateLogger("CoverUpload");

            if (!httpRequest.HasFormContentType)
                return ApiErrors.BadRequest("Expected multipart form data.");

            var form = await httpRequest.ReadFormAsync(ct);
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return ApiErrors.BadRequest("No file provided.");

            var normalizedAssetType = "CoverArt";
            if (!ArtworkScopeService.IsArtworkUploadAllowed(file.ContentType, normalizedAssetType))
                return ApiErrors.BadRequest("Only JPEG and PNG images are accepted.");

            var context = await metadataData.ResolveArtworkContextAsync(entityId, ct);
            var targetEntityId = context.RootWorkId ?? context.WorkId;
            if (targetEntityId is null || targetEntityId == Guid.Empty)
                return ApiErrors.NotFound($"Asset {entityId} not found.");

            var variantId = Guid.NewGuid();
            var localPath = artworkScopeService.BuildArtworkUploadPath(
                "Work",
                targetEntityId.Value,
                normalizedAssetType,
                variantId,
                file.ContentType);

            AssetPathService.EnsureDirectory(localPath);
            await using (var stream = file.OpenReadStream())
            await using (var fs = new FileStream(localPath, FileMode.Create, FileAccess.Write))
            {
                await stream.CopyToAsync(fs, ct);
            }

            var storedAsset = new EntityAsset
            {
                Id = variantId,
                EntityId = targetEntityId.Value.ToString(),
                EntityType = "Work",
                AssetTypeValue = normalizedAssetType,
                ImageUrl = BuildArtworkVariantStreamUrl(variantId),
                LocalImagePath = localPath,
                SourceProvider = "user_upload",
                AssetClassValue = "Artwork",
                StorageLocationValue = "Central",
                OwnerScope = "Work",
                IsPreferred = true,
                IsUserOverride = true,
                IsLocallyExported = false,
                IsPreferredExported = false,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            await entityAssetRepo.UpsertAsync(storedAsset, ct);
            await entityAssetRepo.SetPreferredAsync(storedAsset.Id, ct);
            await artworkScopeService.SyncArtworkCanonicalAsync(targetEntityId.Value, normalizedAssetType, storedAsset, ct);
            await assetExportService.ReconcileArtworkAsync(storedAsset.EntityId, storedAsset.EntityType, storedAsset.AssetTypeValue, ct);

            coverLogger.LogInformation("Cover uploaded for {EntityId} ? {Path}", entityId, localPath);

            await activityRepo.LogAsync(new SystemActivityEntry
            {
                ActionType = SystemActionType.CoverArtSaved,
                EntityId   = entityId,
                EntityType = "MediaAsset",
                Detail     = "Cover art uploaded manually",
            }, ct);

            return Results.Ok(new ArtworkUploadResponse(
                entityId,
                normalizedAssetType,
                storedAsset.Id,
                storedAsset.ImageUrl));
        })
        .WithName("UploadCover")
        .WithSummary("Upload cover art for a media asset.")
        .Produces<ArtworkUploadResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator()
        .DisableAntiforgery();

        // -- POST /metadata/{entityId}/artwork/{scopeId}/{assetType} --------
        group.MapPost("/{entityId:guid}/artwork/{scopeId}/{assetType}", async (
            Guid entityId,
            string scopeId,
            string assetType,
            ICanonicalValueRepository canonicalRepo,
            IEntityAssetRepository entityAssetRepo,
            IAssetExportService assetExportService,
            ILibraryItemRepository libraryItemRepo,
            IMetadataEndpointDataService metadataData,
            ArtworkScopeService artworkScopeService,
            HttpRequest httpRequest,
            CancellationToken ct) =>
        {
            var normalizedAssetType = ArtworkScopeService.NormalizeUploadedArtworkType(assetType);
            if (normalizedAssetType is null)
                return ApiErrors.BadRequest("Artwork type is not supported for scoped upload.");

            if (!httpRequest.HasFormContentType)
                return ApiErrors.BadRequest("Expected multipart form data.");

            var context = await ResolveEditorScopeContextAsync(entityId, canonicalRepo, libraryItemRepo, metadataData, ct);
            if (context is null)
                return ApiErrors.NotFound($"Editor context for {entityId} not found.");

            var scope = context.Scopes.FirstOrDefault(candidate =>
                string.Equals(candidate.ScopeId, scopeId, StringComparison.OrdinalIgnoreCase));
            if (scope is null)
                return ApiErrors.NotFound($"Scope '{scopeId}' was not found for {entityId}.");

            if (!scope.CanEditArtwork || scope.ArtworkOwnerEntityId is null || string.IsNullOrWhiteSpace(scope.ArtworkOwnerEntityKind))
                return ApiErrors.BadRequest($"Scope '{scope.Label}' does not accept artwork uploads.");

            var allowedSlots = ArtworkScopeService.GetScopedArtworkSlots(scope.MediaType, scope.ScopeId);
            if (!allowedSlots.Contains(normalizedAssetType, StringComparer.OrdinalIgnoreCase))
                return ApiErrors.BadRequest($"{normalizedAssetType} is not valid for the {scope.Label} scope.");

            var form = await httpRequest.ReadFormAsync(ct);
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return ApiErrors.BadRequest("No file provided.");

            if (!ArtworkScopeService.IsArtworkUploadAllowed(file.ContentType, normalizedAssetType))
                return ApiErrors.BadRequest(normalizedAssetType == "Logo"
                    ? "Logo uploads must be PNG images."
                    : "Only JPEG and PNG images are accepted.");

            var variantId = Guid.NewGuid();
            var localPath = artworkScopeService.BuildScopedArtworkUploadPath(scope, normalizedAssetType, variantId, file.ContentType);
            if (string.IsNullOrWhiteSpace(localPath))
                return ApiErrors.NotFound($"Could not resolve an artwork folder for the {scope.Label} scope.");

            AssetPathService.EnsureDirectory(localPath);
            await using (var stream = file.OpenReadStream())
            await using (var fs = new FileStream(localPath, FileMode.Create, FileAccess.Write))
            {
                await stream.CopyToAsync(fs, ct);
            }

            var storedAsset = new EntityAsset
            {
                Id = variantId,
                EntityId = scope.ArtworkOwnerEntityId.Value.ToString(),
                EntityType = scope.ArtworkOwnerEntityKind!,
                AssetTypeValue = normalizedAssetType,
                ImageUrl = BuildArtworkVariantStreamUrl(variantId),
                LocalImagePath = localPath,
                SourceProvider = "user_upload",
                AssetClassValue = "Artwork",
                StorageLocationValue = "Central",
                OwnerScope = scope.Label,
                IsPreferred = true,
                IsUserOverride = true,
                IsLocallyExported = false,
                IsPreferredExported = false,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            await entityAssetRepo.UpsertAsync(storedAsset, ct);
            await entityAssetRepo.SetPreferredAsync(storedAsset.Id, ct);
            await artworkScopeService.SyncArtworkCanonicalAsync(scope.ArtworkOwnerEntityId.Value, normalizedAssetType, storedAsset, ct);
            await assetExportService.ReconcileArtworkAsync(storedAsset.EntityId, storedAsset.EntityType, storedAsset.AssetTypeValue, ct);

            return Results.Ok(new ScopedArtworkUploadResponse(
                entityId,
                scope.ScopeId,
                scope.ArtworkOwnerEntityId,
                normalizedAssetType,
                storedAsset.Id,
                storedAsset.ImageUrl));
        })
        .WithName("UploadScopedArtwork")
        .WithSummary("Upload a new artwork variant for a specific editor scope owner.")
        .Produces<ScopedArtworkUploadResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator()
        .DisableAntiforgery();

        // -- POST /metadata/{entityId}/artwork/{scopeId}/{assetType}/from-url -
        group.MapPost("/{entityId:guid}/artwork/{scopeId}/{assetType}/from-url", async (
            Guid entityId,
            string scopeId,
            string assetType,
            CoverFromUrlRequest request,
            ICanonicalValueRepository canonicalRepo,
            IEntityAssetRepository entityAssetRepo,
            IAssetExportService assetExportService,
            ILibraryItemRepository libraryItemRepo,
            IMetadataEndpointDataService metadataData,
            ArtworkScopeService artworkScopeService,
            IHttpClientFactory httpFactory,
            CancellationToken ct) =>
        {
            var normalizedAssetType = ArtworkScopeService.NormalizeUploadedArtworkType(assetType);
            if (normalizedAssetType is null)
                return ApiErrors.BadRequest("Artwork type is not supported for scoped download.");

            if (string.IsNullOrWhiteSpace(request.ImageUrl))
                return ApiErrors.BadRequest("image_url is required.");

            var context = await ResolveEditorScopeContextAsync(entityId, canonicalRepo, libraryItemRepo, metadataData, ct);
            if (context is null)
                return ApiErrors.NotFound($"Editor context for {entityId} not found.");

            var scope = context.Scopes.FirstOrDefault(candidate =>
                string.Equals(candidate.ScopeId, scopeId, StringComparison.OrdinalIgnoreCase));
            if (scope is null)
                return ApiErrors.NotFound($"Scope '{scopeId}' was not found for {entityId}.");

            if (!scope.CanEditArtwork || scope.ArtworkOwnerEntityId is null || string.IsNullOrWhiteSpace(scope.ArtworkOwnerEntityKind))
                return ApiErrors.BadRequest($"Scope '{scope.Label}' does not accept artwork downloads.");

            var allowedSlots = ArtworkScopeService.GetScopedArtworkSlots(scope.MediaType, scope.ScopeId);
            if (!allowedSlots.Contains(normalizedAssetType, StringComparer.OrdinalIgnoreCase))
                return ApiErrors.BadRequest($"{normalizedAssetType} is not valid for the {scope.Label} scope.");

            using var client = httpFactory.CreateClient("cover_download");
            using var response = await client.GetAsync(request.ImageUrl, ct);
            if (!response.IsSuccessStatusCode)
                return ApiErrors.BadRequest($"Failed to download image: {(int)response.StatusCode} {response.ReasonPhrase}");

            var imageBytes = await response.Content.ReadAsByteArrayAsync(ct);
            if (imageBytes.Length == 0)
                return ApiErrors.BadRequest("Downloaded image is empty.");

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (!ArtworkScopeService.IsArtworkUploadAllowed(contentType, normalizedAssetType))
                return ApiErrors.BadRequest(normalizedAssetType == "Logo"
                    ? "Logo uploads must be PNG images."
                    : "Only JPEG and PNG images are accepted.");

            var variantId = Guid.NewGuid();
            var localPath = artworkScopeService.BuildScopedArtworkUploadPath(scope, normalizedAssetType, variantId, contentType);
            if (string.IsNullOrWhiteSpace(localPath))
                return ApiErrors.NotFound($"Could not resolve an artwork folder for the {scope.Label} scope.");

            AssetPathService.EnsureDirectory(localPath);
            await File.WriteAllBytesAsync(localPath, imageBytes, ct);

            var storedAsset = new EntityAsset
            {
                Id = variantId,
                EntityId = scope.ArtworkOwnerEntityId.Value.ToString(),
                EntityType = scope.ArtworkOwnerEntityKind!,
                AssetTypeValue = normalizedAssetType,
                ImageUrl = BuildArtworkVariantStreamUrl(variantId),
                LocalImagePath = localPath,
                SourceProvider = "user_upload",
                AssetClassValue = "Artwork",
                StorageLocationValue = "Central",
                OwnerScope = scope.Label,
                IsPreferred = true,
                IsUserOverride = true,
                IsLocallyExported = false,
                IsPreferredExported = false,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            await entityAssetRepo.UpsertAsync(storedAsset, ct);
            await entityAssetRepo.SetPreferredAsync(storedAsset.Id, ct);
            await artworkScopeService.SyncArtworkCanonicalAsync(scope.ArtworkOwnerEntityId.Value, normalizedAssetType, storedAsset, ct);
            await assetExportService.ReconcileArtworkAsync(storedAsset.EntityId, storedAsset.EntityType, storedAsset.AssetTypeValue, ct);

            return Results.Ok(new ScopedArtworkUploadResponse(
                entityId,
                scope.ScopeId,
                scope.ArtworkOwnerEntityId,
                normalizedAssetType,
                storedAsset.Id,
                storedAsset.ImageUrl));
        })
        .WithName("UploadScopedArtworkFromUrl")
        .WithSummary("Download a new artwork variant from a URL for a specific editor scope owner.")
        .Produces<ScopedArtworkUploadResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        // -- POST /metadata/{entityId}/artwork/{assetType} -------------------
        group.MapPost("/{entityId:guid}/artwork/{assetType}", async (
            Guid entityId,
            string assetType,
            IEntityAssetRepository entityAssetRepo,
            IAssetExportService assetExportService,
            IMetadataEndpointDataService metadataData,
            ArtworkScopeService artworkScopeService,
            HttpRequest httpRequest,
            CancellationToken ct) =>
        {
            var normalizedAssetType = ArtworkScopeService.NormalizeUploadedArtworkType(assetType);
            if (normalizedAssetType is null)
                return ApiErrors.BadRequest("Artwork type must be CoverArt, SquareArt, Background, Banner, or Logo.");

            if (!httpRequest.HasFormContentType)
                return ApiErrors.BadRequest("Expected multipart form data.");

            var form = await httpRequest.ReadFormAsync(ct);
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return ApiErrors.BadRequest("No file provided.");

            if (!ArtworkScopeService.IsArtworkUploadAllowed(file.ContentType, normalizedAssetType))
                return ApiErrors.BadRequest(normalizedAssetType == "Logo"
                    ? "Logo uploads must be PNG images."
                    : "Only JPEG and PNG images are accepted.");

            var context = await metadataData.ResolveArtworkContextAsync(entityId, ct);
            var targetEntityId = context.RootWorkId ?? context.WorkId;
            if (targetEntityId is null || targetEntityId == Guid.Empty)
                return ApiErrors.NotFound($"Asset {entityId} not found.");

            var variantId = Guid.NewGuid();
            var localPath = artworkScopeService.BuildArtworkUploadPath(
                "Work",
                targetEntityId.Value,
                normalizedAssetType,
                variantId,
                file.ContentType);

            AssetPathService.EnsureDirectory(localPath);
            await using (var stream = file.OpenReadStream())
            await using (var fs = new FileStream(localPath, FileMode.Create, FileAccess.Write))
            {
                await stream.CopyToAsync(fs, ct);
            }

            var storedAsset = new EntityAsset
            {
                Id = variantId,
                EntityId = targetEntityId.Value.ToString(),
                EntityType = "Work",
                AssetTypeValue = normalizedAssetType,
                ImageUrl = BuildArtworkVariantStreamUrl(variantId),
                LocalImagePath = localPath,
                SourceProvider = "user_upload",
                AssetClassValue = "Artwork",
                StorageLocationValue = "Central",
                OwnerScope = "Work",
                IsPreferred = true,
                IsUserOverride = true,
                IsLocallyExported = false,
                IsPreferredExported = false,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            await entityAssetRepo.UpsertAsync(storedAsset, ct);
            await entityAssetRepo.SetPreferredAsync(storedAsset.Id, ct);
            await artworkScopeService.SyncArtworkCanonicalAsync(targetEntityId.Value, normalizedAssetType, storedAsset, ct);
            await assetExportService.ReconcileArtworkAsync(storedAsset.EntityId, storedAsset.EntityType, storedAsset.AssetTypeValue, ct);

            return Results.Ok(new ArtworkUploadResponse(
                entityId,
                normalizedAssetType,
                storedAsset.Id,
                storedAsset.ImageUrl));
        })
        .WithName("UploadEntityArtwork")
        .WithSummary("Upload a new artwork variant for a media asset.")
        .Produces<ArtworkUploadResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator()
        .DisableAntiforgery();

        // -- PUT /metadata/artwork/{variantId}/preferred ---------------------
        group.MapPut("/artwork/{variantId:guid}/preferred", async (
            Guid variantId,
            IEntityAssetRepository entityAssetRepo,
            IAssetExportService assetExportService,
            ArtworkScopeService artworkScopeService,
            CancellationToken ct) =>
        {
            var target = await entityAssetRepo.FindByIdAsync(variantId, ct);
            if (target is null)
                return ApiErrors.NotFound($"Artwork variant {variantId} not found.");

            await entityAssetRepo.SetPreferredAsync(variantId, ct);
            target = await entityAssetRepo.FindByIdAsync(variantId, ct);
            if (target is null)
                return ApiErrors.NotFound($"Artwork variant {variantId} not found.");

            await artworkScopeService.SyncArtworkCanonicalAsync(
                Guid.Parse(target.EntityId),
                target.AssetTypeValue,
                target,
                ct);
            await assetExportService.ReconcileArtworkAsync(target.EntityId, target.EntityType, target.AssetTypeValue, ct);

            return Results.Ok(new ArtworkVariantPreferredResponse(
                variantId,
                target.AssetTypeValue,
                target.ImageUrl));
        })
        .WithName("SetPreferredArtwork")
        .WithSummary("Mark an artwork variant as preferred for its slot.")
        .Produces<ArtworkVariantPreferredResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        // -- DELETE /metadata/artwork/{variantId} ----------------------------
        group.MapDelete("/artwork/{variantId:guid}", async (
            Guid variantId,
            IEntityAssetRepository entityAssetRepo,
            IAssetExportService assetExportService,
            ArtworkScopeService artworkScopeService,
            CancellationToken ct) =>
        {
            var target = await entityAssetRepo.FindByIdAsync(variantId, ct);
            if (target is null)
                return ApiErrors.NotFound($"Artwork variant {variantId} not found.");

            var entityId = Guid.Parse(target.EntityId);
            var siblings = await entityAssetRepo.GetByEntityAsync(target.EntityId, target.AssetTypeValue, ct);
            var wasPreferred = target.IsPreferred;
            var ownsLocalFile = string.Equals(target.SourceProvider, "user_upload", StringComparison.OrdinalIgnoreCase)
                || target.IsUserOverride;

            if (ownsLocalFile && !string.IsNullOrWhiteSpace(target.LocalImagePath))
            {
                try
                {
                    if (File.Exists(target.LocalImagePath))
                        File.Delete(target.LocalImagePath);
                }
                catch
                {
                    // Best-effort cleanup. The row delete should still proceed.
                }
            }

            await entityAssetRepo.DeleteAsync(variantId, ct);

            var remaining = siblings
                .Where(asset => asset.Id != variantId)
                .OrderByDescending(asset => asset.IsPreferred)
                .ThenByDescending(asset => asset.CreatedAt)
                .ToList();

            var nextPreferred = remaining.FirstOrDefault();
            if (nextPreferred is not null && (wasPreferred || !remaining.Any(asset => asset.IsPreferred)))
            {
                await entityAssetRepo.SetPreferredAsync(nextPreferred.Id, ct);
                nextPreferred = await entityAssetRepo.FindByIdAsync(nextPreferred.Id, ct);
            }

            await artworkScopeService.SyncArtworkCanonicalAsync(entityId, target.AssetTypeValue, nextPreferred, ct);
            if (nextPreferred is not null)
                await assetExportService.ReconcileArtworkAsync(nextPreferred.EntityId, nextPreferred.EntityType, nextPreferred.AssetTypeValue, ct);
            else
                await assetExportService.ClearArtworkExportAsync(target.EntityId, target.EntityType, target.AssetTypeValue, ct);

            return Results.Ok(new ArtworkVariantDeletedResponse(
                variantId,
                target.AssetTypeValue,
                nextPreferred?.Id));
        })
        .WithName("DeleteArtworkVariant")
        .WithSummary("Delete an artwork variant from the item.")
        .Produces<ArtworkVariantDeletedResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        // -- GET /metadata/wikidata-test ----------------------------------------
        //
        // Diagnostic endpoint for validating Wikidata search.  Directly calls the
        // MediaWiki wbsearchentities API and/or SPARQL bridge lookup so the user
        // can confirm Wikidata connectivity and search accuracy.
        //
        // Query params (at least one required):
        //   ?title=Abaddon's Gate   — title search via wbsearchentities
        //   ?isbn=9780316129077     — ISBN bridge lookup via SPARQL (P212)

        group.MapGet("/wikidata-test", async (
            string? title,
            string? isbn,
            IHttpClientFactory httpFactory,
            IConfigurationLoader configLoader,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(isbn))
                return ApiErrors.BadRequest("Provide ?title= or ?isbn= query parameter.");

            var provConfigs = configLoader.LoadAllProviders();
            var endpointMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pc in provConfigs)
                foreach (var (key, url) in pc.Endpoints)
                    endpointMap.TryAdd(key, url);

            var apiBaseUrl    = endpointMap.GetValueOrDefault("wikidata_api", "");
            var sparqlBaseUrl = endpointMap.GetValueOrDefault("wikidata_sparql", "");

            var results = new Dictionary<string, object?>
            {
                ["api_base_url"]    = apiBaseUrl,
                ["sparql_base_url"] = sparqlBaseUrl,
            };

            // Title search via MediaWiki API.
            if (!string.IsNullOrWhiteSpace(title))
            {
                if (string.IsNullOrWhiteSpace(apiBaseUrl))
                {
                    results["title_error"] = "wikidata_api endpoint not configured";
                }
                else
                {
                    var searchUrl = $"{apiBaseUrl.TrimEnd('/')}" +
                        $"?action=wbsearchentities&search={Uri.EscapeDataString(title)}" +
                        "&type=item&language=en&format=json&limit=5";

                    results["title_search_url"] = searchUrl;

                    try
                    {
                        using var apiClient = httpFactory.CreateClient("wikidata_api");
                        var response = await apiClient.GetAsync(searchUrl, ct);
                        var body     = await response.Content.ReadAsStringAsync(ct);
                        var json     = JsonNode.Parse(body) as JsonObject;
                        var search   = json?["search"]?.AsArray();

                        results["title_result_count"] = search?.Count ?? 0;
                        results["title_results"] = search?.Select(r => new
                        {
                            id          = r?["id"]?.GetValue<string>(),
                            label       = r?["label"]?.GetValue<string>(),
                            description = r?["description"]?.GetValue<string>(),
                        }).ToArray();
                        results["title_qid"] = search?.FirstOrDefault()?["id"]?.GetValue<string>();
                    }
                    catch (Exception ex)
                    {
                        results["title_error"] = ex.Message;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(isbn))
            {
                results["isbn_note"] = "ISBN bridge lookup is handled by the ReconciliationAdapter via Wikidata bridge resolution.";
            }

            return Results.Ok(results);
        })
        .WithName("WikidataTest")
        .WithSummary("Diagnostic: test Wikidata search by title or ISBN bridge lookup.")
        .Produces(StatusCodes.Status200OK)
        .RequireAdmin();


        // â"€â"€ POST /metadata/search-all â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€
        //
        // Fan-out search: queries ALL eligible providers concurrently and returns
        // merged results grouped by provider. Powers the shared media editor search flow.

        group.MapPost("/search-all", async (
            FanOutSearchRequest request,
            IEnumerable<IExternalMetadataProvider> providers,
            IConfigurationLoader configLoader,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var searchLogger = loggerFactory.CreateLogger("MetadataFanOutSearch");

            if (string.IsNullOrWhiteSpace(request.Query))
                return ApiErrors.BadRequest("query is required.");

            var mediaType = Domain.Enums.MediaType.Unknown;
            if (!string.IsNullOrEmpty(request.MediaType))
                Enum.TryParse(request.MediaType, ignoreCase: true, out mediaType);

            var limit = Math.Clamp(request.MaxResultsPerProvider, 1, 25);
            var providerList = providers.ToList();

            // Filter providers
            var eligibleProviders = providerList
                .Where(p => p.CanHandle(mediaType))
                .Where(p => string.IsNullOrEmpty(request.ProviderId)
                    || string.Equals(p.ProviderId.ToString(), request.ProviderId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(p.Name, request.ProviderId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            searchLogger.LogInformation(
                "Fan-out search: query={Query}, mediaType={MediaType}, providers=[{Providers}]",
                request.Query, mediaType,
                string.Join(", ", eligibleProviders.Select(p => p.Name)));

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Run all provider searches concurrently with per-provider timeout.
            var tasks = eligibleProviders.Select(async provider =>
            {
                var provConfig = configLoader.LoadProvider(provider.Name);
                var baseUrl = provConfig?.Endpoints.Values.FirstOrDefault() ?? string.Empty;

                var lookupRequest = new ProviderLookupRequest
                {
                    EntityId   = Guid.Empty,
                    EntityType = EntityType.MediaAsset,
                    MediaType  = mediaType,
                    Title      = request.Query,
                    BaseUrl    = baseUrl,
                };

                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(TimeSpan.FromSeconds(10));

                    var results = await provider.SearchAsync(lookupRequest, limit, cts.Token);

                    return new ProviderSearchResult
                    {
                        ProviderId   = provider.ProviderId.ToString(),
                        ProviderName = provider.Name,
                        Items = results.Select(r => new FanOutSearchResultItem
                        {
                            Title          = r.Title,
                            Author         = r.Author,
                            Description    = r.Description,
                            Year           = r.Year,
                            ThumbnailUrl   = r.ThumbnailUrl,
                            ProviderItemId = r.ProviderItemId,
                            Confidence     = r.Confidence,
                            ResultType     = r.ResultType,
                            RawFields      = BuildRawFields(r),
                        }).ToList(),
                    };
                }
                catch (OperationCanceledException)
                {
                    return new ProviderSearchResult
                    {
                        ProviderId   = provider.ProviderId.ToString(),
                        ProviderName = provider.Name,
                        Error        = "Timeout (10s)",
                    };
                }
                catch (Exception ex)
                {
                    searchLogger.LogWarning(ex,
                        "Fan-out search: provider {Provider} failed", provider.Name);
                    return new ProviderSearchResult
                    {
                        ProviderId   = provider.ProviderId.ToString(),
                        ProviderName = provider.Name,
                        Error        = ex.Message,
                    };
                }
            }).ToList();

            var providerResults = await Task.WhenAll(tasks);
            stopwatch.Stop();

            var response = new FanOutSearchResponse
            {
                Results            = providerResults.ToList(),
                TotalProviders     = eligibleProviders.Count,
                RespondedProviders = providerResults.Count(r => r.Error is null),
                ElapsedMs          = stopwatch.Elapsed.TotalMilliseconds,
            };

            return Results.Ok(response);
        })
        .WithName("SearchMetadataFanOut")
        .WithSummary("Fan-out search across all eligible providers. Admin or Curator.")
        .Produces<FanOutSearchResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAdminOrCurator();

        // -- GET /metadata/{entityId}/search-cache -------------------------
        group.MapGet("/{entityId:guid}/search-cache", async (
            Guid entityId,
            ISearchResultsCacheRepository cache) =>
        {
            var json = await cache.FindAsync(entityId, maxAgeDays: 30);
            return json is not null
                ? Results.Ok(new SearchResultsCacheResponse(json))
                : ApiErrors.NotFound($"No cached search results found for entity {entityId}.");
        })
        .WithName("GetSearchResultsCache")
        .WithSummary("Retrieve cached fan-out search results for an entity (30-day TTL)")
        .Produces<SearchResultsCacheResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        // -- PUT /metadata/{entityId}/search-cache -------------------------
        group.MapPut("/{entityId:guid}/search-cache", async (
            Guid entityId,
            SearchCacheUpsertRequest body,
            ISearchResultsCacheRepository cache) =>
        {
            if (string.IsNullOrEmpty(body.ResultsJson))
                return ApiErrors.BadRequest("results_json is required");
            await cache.UpsertAsync(entityId, body.ResultsJson);
            return Results.NoContent();
        })
        .WithName("PutSearchResultsCache")
        .WithSummary("Cache fan-out search results for an entity")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAdminOrCurator();

        // â"€â"€ GET /metadata/canonical/{entityId} â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€
        //
        // Returns all current canonical values for an entity with confidence,
        // provider attribution, and user-lock status.

        group.MapGet("/canonical/{entityId:guid}", async (
            Guid entityId,
            ICanonicalValueRepository canonicalRepo,
            IMetadataClaimRepository claimRepo,
            IMediaAssetRepository assetRepo,
            IEnumerable<IExternalMetadataProvider> providers,
            CancellationToken ct) =>
        {
            // entityId may be a work ID (from the libraryItem) — resolve to the
            // underlying media asset ID where canonical values are stored.
            var resolvedId = entityId;
            var canonicals = await canonicalRepo.GetByEntityAsync(resolvedId, ct);
            if (canonicals.Count == 0)
            {
                // Try resolving as a work ID ? find the first media asset.
                var asset = await assetRepo.FindFirstByWorkIdAsync(resolvedId, ct);
                if (asset is not null)
                {
                    resolvedId = asset.Id;
                    canonicals = await canonicalRepo.GetByEntityAsync(resolvedId, ct);
                }
            }

            if (canonicals.Count == 0)
                return ApiErrors.NotFound($"No canonical values found for entity {entityId}.");

            // Load claims to determine user-lock and conflict status per field.
            var allClaims = await claimRepo.GetByEntityAsync(resolvedId, ct);
            var providerList = providers.ToList();

            var fields = canonicals.Select(cv =>
            {
                // Find the winning claim for this field.
                var fieldClaims = allClaims
                    .Where(c => string.Equals(c.ClaimKey, cv.Key, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var isUserLocked = fieldClaims.Any(c => c.IsUserLocked);

                // Check for conflict: multiple claims with substantially different values.
                var distinctValues = fieldClaims
                    .Select(c => c.ClaimValue)
                    .Where(v => !string.IsNullOrEmpty(v))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var isConflicted = distinctValues.Count > 1;

                // Find provider name from the winning claim's provider ID.
                string? providerName = null;
                var winningClaim = fieldClaims
                    .OrderByDescending(c => c.IsUserLocked)
                    .ThenByDescending(c => c.Confidence)
                    .FirstOrDefault();
                if (winningClaim is not null)
                {
                    var matchedProvider = providerList.FirstOrDefault(
                        p => p.ProviderId == winningClaim.ProviderId);
                    providerName = matchedProvider?.Name ?? winningClaim.ProviderId.ToString();
                }

                return new CanonicalFieldDto
                {
                    Key          = cv.Key,
                    Value        = cv.Value,
                    Confidence   = (winningClaim?.Confidence ?? 0.0),
                    ProviderName = providerName,
                    IsUserLocked = isUserLocked,
                    IsConflicted = isConflicted,
                };
            }).ToList();

            return Results.Ok(fields);
        })
        .WithName("GetCanonicalValues")
        .WithSummary("Get all canonical values for an entity with provenance. Curator+.")
        .Produces<List<CanonicalFieldDto>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        // â"€â"€ POST /metadata/{entityId}/cover-from-url â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€
        //
        // Downloads a cover image from a provider URL, saves the managed artwork,
        // generates renditions + palette metadata, and updates canonical values.

        group.MapPost("/{entityId:guid}/cover-from-url", async (
            Guid entityId,
            CoverFromUrlRequest request,
            IMediaAssetRepository assetRepo,
            IWorkRepository workRepo,
            IEntityAssetRepository entityAssetRepo,
            IAssetExportService assetExportService,
            AssetPathService assetPathService,
            ArtworkScopeService artworkScopeService,
            IHttpClientFactory httpFactory,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("CoverFromUrl");

            if (string.IsNullOrWhiteSpace(request.ImageUrl))
                return ApiErrors.BadRequest("image_url is required.");

            var asset = await assetRepo.FindByIdAsync(entityId, ct);
            if (asset is null)
                return ApiErrors.NotFound($"Media asset {entityId} not found.");

            var lineage = await workRepo.GetLineageByAssetAsync(entityId, ct);
            var ownerEntityId = lineage?.TargetForParentScope ?? entityId;

            try
            {
                using var client = httpFactory.CreateClient("cover_download");
                using var response = await client.GetAsync(request.ImageUrl, ct);
                response.EnsureSuccessStatusCode();

                var imageBytes = await response.Content.ReadAsByteArrayAsync(ct);

                if (imageBytes.Length == 0)
                    return ApiErrors.BadRequest("Downloaded image is empty.");

                var contentType = response.Content.Headers.ContentType?.MediaType;
                if (!ArtworkScopeService.IsArtworkUploadAllowed(contentType, "CoverArt"))
                    return ApiErrors.BadRequest("Only JPEG and PNG images are accepted.");

                var variantId = Guid.NewGuid();
                var coverPath = artworkScopeService.BuildArtworkUploadPath("Work", ownerEntityId, "CoverArt", variantId, contentType);
                AssetPathService.EnsureDirectory(coverPath);
                await File.WriteAllBytesAsync(coverPath, imageBytes, ct);

                logger.LogInformation(
                    "Cover downloaded from URL for entity {Id}: {Size} bytes â†’ {Path}",
                    entityId, imageBytes.Length, coverPath);

                var storedAsset = new EntityAsset
                {
                    Id = variantId,
                    EntityId = ownerEntityId.ToString(),
                    EntityType = "Work",
                    AssetTypeValue = "CoverArt",
                    ImageUrl = BuildArtworkVariantStreamUrl(variantId),
                    LocalImagePath = coverPath,
                    SourceProvider = "user_upload",
                    AssetClassValue = "Artwork",
                    StorageLocationValue = "Central",
                    OwnerScope = "Work",
                    IsPreferred = true,
                    IsUserOverride = true,
                    IsLocallyExported = false,
                    IsPreferredExported = false,
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                ArtworkVariantHelper.StampMetadataAndRenditions(storedAsset, assetPathService);

                await entityAssetRepo.UpsertAsync(storedAsset, ct);
                await entityAssetRepo.SetPreferredAsync(storedAsset.Id, ct);
                await artworkScopeService.SyncArtworkCanonicalAsync(ownerEntityId, "CoverArt", storedAsset, ct);
                await assetExportService.ReconcileArtworkAsync(storedAsset.EntityId, storedAsset.EntityType, storedAsset.AssetTypeValue, ct);

                return Results.Ok(new CoverFromUrlResponse(
                    entityId,
                    storedAsset.Id,
                    coverPath,
                    storedAsset.PrimaryHex,
                    storedAsset.SecondaryHex,
                    storedAsset.AccentHex));
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "Failed to download cover from URL for entity {Id}", entityId);
                return ApiErrors.BadRequest($"Failed to download image: {ex.Message}");
            }
        })
        .WithName("CoverFromUrl")
        .WithSummary("Download cover art from a URL and rebuild measured artwork metadata. Curator+.")
        .Produces<CoverFromUrlResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        // -- POST /metadata/labels/resolve ---------------------------------
        group.MapPost("/labels/resolve", async (
            LabelResolveRequest request,
            IQidLabelRepository qidLabelRepo,
            CancellationToken ct) =>
        {
            if (request.Qids is null || request.Qids.Count == 0)
                return Results.Ok(new Dictionary<string, LabelResolveEntry>());

            var labels = await qidLabelRepo.GetLabelDetailsAsync(request.Qids, ct);
            var result = labels.ToDictionary(
                l => l.Qid,
                l => new LabelResolveEntry
                {
                    Label       = l.Label,
                    Description = l.Description,
                    EntityType  = l.EntityType,
                });

            return Results.Ok(result);
        })
        .WithName("ResolveLabels")
        .WithSummary("Batch-resolve Wikidata QIDs to display labels from the local cache.")
        .Produces<Dictionary<string, LabelResolveEntry>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        // -- GET /metadata/{qid}/aliases ---------------------------------------
        //
        // Returns the Wikidata label and all aliases for a given QID.
        // Useful for search disambiguation, alternate title lookup, and
        // populating the Inspector's alias chips.

        group.MapGet("/{qid}/aliases", async (
            string qid,
            Tuvima.Wikidata.WikidataReconciler? reconciler,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(qid) || !qid.StartsWith("Q", StringComparison.OrdinalIgnoreCase))
                return ApiErrors.BadRequest("qid must be a valid Wikidata QID starting with 'Q'.");

            if (reconciler is null)
                return Results.Ok(new WikidataAliasesResponse(qid, null, Array.Empty<string>()));

            try
            {
                var entities = await reconciler.GetEntitiesAsync([qid], "en", ct);

                if (!entities.TryGetValue(qid, out var entity))
                    return Results.Ok(new WikidataAliasesResponse(qid, null, Array.Empty<string>()));

                var resultLabel = entity.Label;
                var resultAliases = (IReadOnlyList<string>)(entity.Aliases ?? []);

                // Edition detection: if this QID is an edition/translation (has P629),
                // resolve the parent work and return the work's aliases instead.
                // Edition entities have sparse aliases; work entities carry the popular
                // names (e.g. "1984" is an alias on Q208460 the novel, not on the
                // audiobook edition entity).
                var props = await reconciler.GetPropertiesAsync(
                    [qid], ["P629"], "en", ct);
                if (props.TryGetValue(qid, out var qidProps)
                    && qidProps.TryGetValue("P629", out var p629Values)
                    && p629Values.Count > 0)
                {
                    // P629 value is an entity reference — extract the parent work QID.
                    var p629Val = p629Values[0].Value;
                    var workQid = p629Val?.EntityId ?? p629Val?.RawValue;
                    if (workQid is not null)
                    {
                        // Strip entity URI prefix if present.
                        var slashIdx = workQid.LastIndexOf('/');
                        if (slashIdx >= 0) workQid = workQid[(slashIdx + 1)..];

                        if (workQid.StartsWith("Q", StringComparison.OrdinalIgnoreCase))
                        {
                            var workEntities = await reconciler.GetEntitiesAsync(
                                [workQid], "en", ct);
                            if (workEntities.TryGetValue(workQid, out var workEntity))
                            {
                                resultLabel = workEntity.Label;
                                resultAliases = (IReadOnlyList<string>)(workEntity.Aliases ?? []);
                            }
                        }
                    }
                }

                return Results.Ok(new WikidataAliasesResponse(
                    qid,
                    resultLabel,
                    resultAliases));
            }
            catch (Exception)
            {
                return Results.Ok(new WikidataAliasesResponse(qid, null, Array.Empty<string>()));
            }
        })
        .WithName("GetWikidataAliases")
        .WithSummary("Returns the Wikidata label and all aliases for a given QID.")
        .Produces<WikidataAliasesResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAnyRole();

        return app;
    }

    private static async Task<EditorScopeContext?> ResolveEditorScopeContextAsync(
        Guid entityId,
        ICanonicalValueRepository canonicalRepo,
        ILibraryItemRepository libraryItemRepo,
        IMetadataEndpointDataService metadataData,
        CancellationToken ct)
    {
        var launch = await metadataData.ResolveEditorLaunchAsync(entityId, ct);
        if (launch is null)
            return null;

        var launchDetail = await libraryItemRepo.GetDetailAsync(launch.WorkId, ct);
        var launchCanonicals = await canonicalRepo.GetByEntityAsync(launch.WorkId, ct);
        var canonicalMap = BuildLatestCanonicalMap(launchCanonicals);

        var rootDetail = launch.RootWorkId != Guid.Empty && launch.RootWorkId != launch.WorkId
            ? await libraryItemRepo.GetDetailAsync(launch.RootWorkId, ct)
            : launchDetail;

        var rootCanonicals = launch.RootWorkId != Guid.Empty && launch.RootWorkId != launch.WorkId
            ? await canonicalRepo.GetByEntityAsync(launch.RootWorkId, ct)
            : launchCanonicals;

        var rootCanonicalMap = BuildLatestCanonicalMap(rootCanonicals);

        var artistOwnerId = string.Equals(launch.MediaType, "Music", StringComparison.OrdinalIgnoreCase)
            ? await metadataData.ResolveArtistArtworkOwnerAsync(
                launch.RepresentativeAssetId,
                StringHelpers.FirstNonBlankOr(string.Empty, GetCanonicalValue(rootCanonicalMap, "artist"), GetCanonicalValue(canonicalMap, "artist")),
                ct)
            : null;

        var scopes = BuildEditorScopes(launch, launchDetail, canonicalMap, rootDetail, rootCanonicalMap, artistOwnerId);
        if (scopes.Count == 0)
            return null;

        var scopeIdentitySummaries = new Dictionary<Guid, MediaEditorIdentitySummaryEnvelope>();
        foreach (var scope in scopes
                     .Where(scope => !string.Equals(scope.ScopeId, "file", StringComparison.OrdinalIgnoreCase))
                     .DistinctBy(scope => scope.FieldEntityId))
        {
            LibraryItemDetail? scopeDetail;
            if (scope.FieldEntityId == launch.WorkId)
                scopeDetail = launchDetail;
            else if (scope.FieldEntityId == launch.RootWorkId)
                scopeDetail = rootDetail;
            else
                scopeDetail = await libraryItemRepo.GetDetailAsync(scope.FieldEntityId, ct);

            scopeIdentitySummaries[scope.FieldEntityId] = BuildIdentitySummary(scopeDetail);
        }

        var initialScope = GetDefaultEditorScope(launch, scopes);
        var initialScopeResolution = scopes.FirstOrDefault(scope => string.Equals(scope.ScopeId, initialScope, StringComparison.OrdinalIgnoreCase))
            ?? scopes[0];
        var editorMode = IsContainerEditorLaunch(launch) ? "container" : "singular";
        var displayOverrides = await metadataData.GetDisplayOverridesAsync(initialScopeResolution.FieldEntityId, ct);

        return new EditorScopeContext(
            launch.LaunchEntityId,
            launch.LaunchEntityKind,
            launch.MediaType,
            editorMode,
            BuildEditorAvailableTabs(editorMode, launch.MediaType, initialScopeResolution.ScopeId, initialScopeResolution.CanEditArtwork, launch.RepresentativeMediaFilePath),
            BuildContentTabLabel(editorMode, launch.MediaType),
            !string.Equals(editorMode, "container", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(launch.RepresentativeMediaFilePath),
            BuildFileMetadataSyncStatus(launch.RepresentativeMediaFilePath, launch.RepresentativeWritebackStatus),
            BuildCurrentTargetSummary(initialScopeResolution),
            scopeIdentitySummaries.TryGetValue(initialScopeResolution.FieldEntityId, out var initialIdentitySummary)
                ? initialIdentitySummary
                : BuildIdentitySummary(launchDetail),
            BuildFieldLockMap(launch.MediaType, initialScopeResolution.ScopeId),
            BuildDisplayOverrideKeys(launch.MediaType),
            displayOverrides,
            initialScope,
            scopes,
            scopeIdentitySummaries);
    }

    private static bool IsContainerEditorMediaType(string? mediaType) =>
        NormalizeEditorMediaType(mediaType) is "TV" or "Music";

    private static bool IsContainerEditorLaunch(EditorLaunchContext launch) =>
        IsContainerEditorMediaType(launch.MediaType)
        && !string.Equals(launch.WorkKind, "child", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> BuildEditorAvailableTabs(
        string editorMode,
        string mediaType,
        string scopeId,
        bool canEditArtwork,
        string? representativeMediaFilePath)
    {
        var normalized = NormalizeEditorMediaType(mediaType);
        if (string.Equals(editorMode, "container", StringComparison.OrdinalIgnoreCase))
        {
            return normalized switch
            {
                "TV" => ["details", "episodes", "artwork", "links", "file", "history"],
                "Music" => ["details", "tracks", "artwork", "links", "file", "history"],
                _ => ["details", "links", "history"],
            };
        }

        var tabs = new List<string> { "details" };
        if (normalized == "Audiobooks")
            tabs.Add("contents");

        if (canEditArtwork)
            tabs.Add("artwork");

        tabs.Add("links");

        if (!string.IsNullOrWhiteSpace(representativeMediaFilePath))
            tabs.Add("file");

        tabs.Add("history");

        return tabs;
    }

    private static string? BuildContentTabLabel(string editorMode, string mediaType) =>
        !string.Equals(editorMode, "container", StringComparison.OrdinalIgnoreCase)
            ? null
            : NormalizeEditorMediaType(mediaType) switch
            {
                "TV" => "Episodes",
                "Music" => "Tracks",
                _ => null,
            };

    private static MediaEditorTargetSummaryEnvelope BuildCurrentTargetSummary(EditorScopeResolution scope) =>
        new(
            scope.Label,
            scope.DisplayTitle,
            scope.DisplaySubtitle);

    private static MediaEditorIdentitySummaryEnvelope BuildIdentitySummary(LibraryItemDetail? detail) =>
        new(
            detail?.RetailProviderName,
            detail?.RetailProviderItemId,
            detail?.MatchSource,
            detail?.MatchMethod,
            detail?.WikidataQid,
            detail?.WikidataStatus,
            detail?.MatchLevel,
            detail?.UniverseSummary?.UniverseName,
            detail?.UniverseSummary?.UniverseQid,
            detail?.UniverseSummary?.Stage3Status);

    private static string BuildFileMetadataSyncStatus(string? filePath, string? writebackStatus)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return "No file";

        return (writebackStatus ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "ok" => "Synced to canonical",
            "retry" => "Sync retry scheduled",
            "failed" => "Sync failed",
            _ => "Not synced",
        };
    }

    private static Dictionary<string, bool> BuildFieldLockMap(string mediaType, string scopeId)
    {
        var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in GetLockedFieldKeys(mediaType, scopeId))
            map[key] = true;

        return map;
    }

    private static IReadOnlyList<string> GetLockedFieldKeys(string mediaType, string scopeId) => [];

    private static IReadOnlyList<string> BuildDisplayOverrideKeys(string mediaType) =>
        NormalizeEditorMediaType(mediaType) switch
        {
            "Movies" or "TV" => ["title", "tagline", "description", "sort_title"],
            _ => ["title", "description", "sort_title"],
        };

    private static List<EditorScopeResolution> BuildEditorScopes(
        EditorLaunchContext launch,
        LibraryItemDetail? detail,
        IReadOnlyDictionary<string, string> canonicalMap,
        LibraryItemDetail? rootDetail,
        IReadOnlyDictionary<string, string> rootCanonicalMap,
        Guid? artistOwnerId)
    {
        var scopes = new List<EditorScopeResolution>();
        var mediaType = NormalizeEditorMediaType(launch.MediaType);
        var itemTitle = StringHelpers.FirstNonBlankOr(string.Empty, detail?.Title, GetCanonicalValue(canonicalMap, "title"), "Item");
        var rootTitle = StringHelpers.FirstNonBlankOr(string.Empty, rootDetail?.Title, GetCanonicalValue(rootCanonicalMap, "title"), itemTitle);
        var itemYear = StringHelpers.FirstNonBlankOr(string.Empty, detail?.Year, GetCanonicalValue(canonicalMap, "year"));
        var rootYear = StringHelpers.FirstNonBlankOr(string.Empty, rootDetail?.Year, GetCanonicalValue(rootCanonicalMap, "year"), itemYear);
        var showName = StringHelpers.FirstNonBlankOr(string.Empty, GetCanonicalValue(rootCanonicalMap, "title"), GetCanonicalValue(canonicalMap, "show_name"), detail?.ShowName, rootTitle, itemTitle);
        var seasonNumber = StringHelpers.FirstNonBlankOr(string.Empty, GetCanonicalValue(canonicalMap, "season_number"), detail?.SeasonNumber);
        var episodeNumber = StringHelpers.FirstNonBlankOr(string.Empty, GetCanonicalValue(canonicalMap, "episode_number"), detail?.EpisodeNumber);
        var episodeTitle = StringHelpers.FirstNonBlankOr(string.Empty,
            detail?.EpisodeTitle,
            GetCanonicalValue(canonicalMap, "episode_title"),
            GetCanonicalValue(canonicalMap, "title"),
            itemTitle,
            "Episode");
        var artistName = StringHelpers.FirstNonBlankOr(string.Empty,
            GetCanonicalValue(rootCanonicalMap, "artist"),
            GetCanonicalValue(canonicalMap, "artist"),
            rootDetail?.Author,
            detail?.Author,
            itemTitle);
        var albumName = StringHelpers.FirstNonBlankOr(string.Empty,
            GetCanonicalValue(canonicalMap, "album"),
            GetCanonicalValue(rootCanonicalMap, "album"),
            rootTitle,
            itemTitle,
            "Album");
        var seriesName = StringHelpers.FirstNonBlankOr(string.Empty, GetCanonicalValue(canonicalMap, "series"), rootTitle, detail?.Series);
        var seasonLabel = string.IsNullOrWhiteSpace(seasonNumber) ? "Season" : $"Season {seasonNumber}";
        var episodeLabel = !string.IsNullOrWhiteSpace(episodeNumber)
            ? $"Episode {episodeNumber}"
            : "Episode";
        var rootWorkId = launch.RootWorkId == Guid.Empty ? launch.WorkId : launch.RootWorkId;
        var hasParentScope = launch.ParentWorkId.HasValue && launch.ParentWorkId.Value != Guid.Empty;
        var hasDistinctRoot = rootWorkId != Guid.Empty && rootWorkId != launch.WorkId;
        var launchWorkKind = launch.WorkKind.Trim().ToLowerInvariant();
        var isParentLaunch = string.Equals(launchWorkKind, "parent", StringComparison.OrdinalIgnoreCase);
        var containerFolder = ArtworkScopeService.GetContainerFolderPath(launch.RepresentativeMediaFilePath);
        var seriesFolder = ArtworkScopeService.GetSeriesFolderPath(launch.RepresentativeMediaFilePath);
        var seasonFolder = ArtworkScopeService.GetSeasonFolderPath(launch.RepresentativeMediaFilePath);
        var artistFolder = ArtworkScopeService.GetArtistFolderPath(launch.RepresentativeMediaFilePath);

        switch (mediaType)
        {
            case "TV":
                var isEpisodeLaunch = string.Equals(launchWorkKind, "child", StringComparison.OrdinalIgnoreCase)
                    && hasParentScope
                    && launch.ParentWorkId!.Value != rootWorkId;
                var isSeasonLaunch = isParentLaunch && hasParentScope;
                var seasonWorkId = isSeasonLaunch
                    ? launch.WorkId
                    : isEpisodeLaunch && hasParentScope
                        ? launch.ParentWorkId!.Value
                        : Guid.Empty;

                scopes.Add(new EditorScopeResolution(
                    "series",
                    "Series",
                    0,
                    hasDistinctRoot ? rootWorkId : launch.WorkId,
                    "Work",
                    hasDistinctRoot ? rootWorkId : launch.WorkId,
                    "Work",
                    showName,
                    rootYear,
                    showName,
                    "show",
                    "Series metadata and show artwork live here.",
                    null,
                    CanEditFields: true,
                    CanEditArtwork: true,
                    MediaType: mediaType,
                    ArtworkFolderPath: seriesFolder ?? containerFolder,
                    RepresentativeMediaFilePath: launch.RepresentativeMediaFilePath));

                if (seasonWorkId != Guid.Empty)
                {
                    scopes.Add(new EditorScopeResolution(
                        "season",
                        "Season",
                        1,
                        seasonWorkId,
                        "Work",
                        seasonWorkId,
                        "Work",
                        seasonLabel,
                        showName,
                        seasonLabel,
                        "show",
                        "Season metadata and season artwork live here.",
                        "Series artwork is managed on the Series scope.",
                        CanEditFields: true,
                        CanEditArtwork: true,
                        MediaType: mediaType,
                        ArtworkFolderPath: seasonFolder ?? containerFolder,
                        RepresentativeMediaFilePath: launch.RepresentativeMediaFilePath));
                }

                if (isEpisodeLaunch)
                {
                    scopes.Add(new EditorScopeResolution(
                        "episode",
                        "Episode",
                        2,
                        launch.WorkId,
                        "Work",
                        launch.WorkId,
                        "Work",
                        episodeTitle,
                        !string.IsNullOrWhiteSpace(episodeNumber) ? episodeLabel : seasonLabel,
                        episodeTitle,
                        "show_episode",
                        "Episode metadata lives here. Show artwork stays on the series.",
                        "Show artwork is managed on the Series scope.",
                        CanEditFields: true,
                        CanEditArtwork: true,
                        MediaType: mediaType,
                        ArtworkFolderPath: null,
                        RepresentativeMediaFilePath: launch.RepresentativeMediaFilePath));
                }
                break;

            case "Music":
                scopes.Add(new EditorScopeResolution(
                    "album",
                    "Album",
                    0,
                    rootWorkId,
                    "Work",
                    rootWorkId,
                    "Work",
                    albumName,
                    artistName,
                    albumName,
                    "album",
                    "Album metadata, cover art, and square art live here.",
                    null,
                    CanEditFields: true,
                    CanEditArtwork: true,
                    MediaType: mediaType,
                    ArtworkFolderPath: containerFolder,
                    RepresentativeMediaFilePath: launch.RepresentativeMediaFilePath));

                if (launch.WorkId != rootWorkId || hasParentScope)
                {
                    scopes.Add(new EditorScopeResolution(
                        "track",
                        "Track",
                        1,
                        launch.WorkId,
                        "Work",
                        null,
                        null,
                        itemTitle,
                        albumName,
                        itemTitle,
                        "track",
                        "Track metadata lives here. Artwork is inherited from the album.",
                        "Track artwork is inherited from the album.",
                        CanEditFields: true,
                        CanEditArtwork: false,
                        MediaType: mediaType,
                        ArtworkFolderPath: null,
                        RepresentativeMediaFilePath: launch.RepresentativeMediaFilePath));
                }
                break;

            case "Movies":
                scopes.Add(new EditorScopeResolution(
                    "item",
                    "Movie",
                    0,
                    launch.WorkId,
                    "Work",
                    launch.WorkId,
                    "Work",
                    itemTitle,
                    StringHelpers.FirstNonBlankOr(string.Empty, detail?.Director, itemYear),
                    itemTitle,
                    "movie_identity",
                    "Movie metadata and artwork live here.",
                    null,
                    CanEditFields: true,
                    CanEditArtwork: true,
                    MediaType: mediaType,
                    ArtworkFolderPath: containerFolder,
                    RepresentativeMediaFilePath: launch.RepresentativeMediaFilePath));
                break;

            case "Comics":
            case "Audiobooks":
            case "Books":
            default:
                scopes.Add(new EditorScopeResolution(
                    "item",
                    mediaType switch
                    {
                        "Comics" => "Comic",
                        "Audiobooks" => "Audiobook",
                        _ => "Book",
                    },
                    0,
                    launch.WorkId,
                    "Work",
                    launch.WorkId,
                    "Work",
                    itemTitle,
                    StringHelpers.FirstNonBlankOr(string.Empty, seriesName, itemYear),
                    itemTitle,
                    mediaType switch
                    {
                        "Comics" => "issue",
                        "Audiobooks" => "audiobook_identity",
                        _ => "book_identity",
                    },
                    "Item metadata and artwork live here.",
                    null,
                    CanEditFields: true,
                    CanEditArtwork: true,
                    MediaType: mediaType,
                    ArtworkFolderPath: containerFolder,
                    RepresentativeMediaFilePath: launch.RepresentativeMediaFilePath));
                break;
        }

        if (!IsContainerEditorMediaType(mediaType) || string.Equals(launch.WorkKind, "child", StringComparison.OrdinalIgnoreCase))
        {
            scopes.Add(new EditorScopeResolution(
                "file",
                "File",
                scopes.Count,
                launch.WorkId,
                "Work",
                null,
                null,
                Path.GetFileName(launch.RepresentativeMediaFilePath) ?? "File",
                launch.RepresentativeMediaFilePath,
                "File",
                mediaType switch
                {
                    "Movies" => "movie_identity",
                    "Comics" => "issue",
                    "Audiobooks" => "audiobook_identity",
                    _ => "book_identity",
                },
                "File inspection and technical details for the concrete media file.",
                "File scope is read-only in the edit panel.",
                CanEditFields: false,
                CanEditArtwork: false,
                MediaType: mediaType,
                ArtworkFolderPath: null,
                RepresentativeMediaFilePath: launch.RepresentativeMediaFilePath));
        }

        return scopes;
    }

    private static string GetDefaultEditorScope(EditorLaunchContext launch, IReadOnlyList<EditorScopeResolution> scopes)
    {
        var launchWorkKind = launch.WorkKind.Trim().ToLowerInvariant();
        var preferredScopeId = NormalizeEditorMediaType(launch.MediaType) switch
        {
            "TV" when string.Equals(launchWorkKind, "child", StringComparison.OrdinalIgnoreCase) => "episode",
            "TV" when string.Equals(launchWorkKind, "parent", StringComparison.OrdinalIgnoreCase) && launch.ParentWorkId.HasValue => "season",
            "TV" => "series",
            "Music" when string.Equals(launchWorkKind, "child", StringComparison.OrdinalIgnoreCase) => "track",
            "Music" => "album",
            "Movies" or "Comics" or "Books" or "Audiobooks" => "item",
            _ => scopes[0].ScopeId,
        };

        return scopes.FirstOrDefault(scope => string.Equals(scope.ScopeId, preferredScopeId, StringComparison.OrdinalIgnoreCase))?.ScopeId
            ?? scopes[0].ScopeId;
    }

    private static IReadOnlyDictionary<string, string> BuildLatestCanonicalMap(
        IEnumerable<CanonicalValue> canonicals) =>
        canonicals
            .Where(field => !string.IsNullOrWhiteSpace(field.Key))
            .GroupBy(field => field.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.LastScoredAt).First().Value,
                StringComparer.OrdinalIgnoreCase);

    internal static string? GetCanonicalValue(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    internal static string NormalizeEditorMediaType(string? mediaType) =>
        (mediaType ?? string.Empty).Trim() switch
        {
            "Book" => "Books",
            "Comic" => "Comics",
            "" => "Books",
            var value => value,
        };

    private static Guid? TryParseGuid(string? value) =>
        Guid.TryParse(value, out var parsed) ? parsed : null;

    private static void AddParsedGuid(List<Guid> ids, string? value)
    {
        if (Guid.TryParse(value, out var parsed) && !ids.Contains(parsed))
            ids.Add(parsed);
    }

    private static async Task<string?> ResolveAssetWikidataQidAsync(
        Guid entityId,
        IWorkRepository workRepo,
        ICanonicalValueRepository canonicalRepo,
        CancellationToken ct)
    {
        var lineage = await workRepo.GetLineageByAssetAsync(entityId, ct);
        var qidEntityId = lineage?.WorkId ?? entityId;
        var canonicals = await canonicalRepo.GetByEntityAsync(qidEntityId, ct);

        return canonicals
            .FirstOrDefault(c => c.Key is "wikidata_qid"
                && !string.IsNullOrEmpty(c.Value)
                && !c.Value.StartsWith("NF", StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }

    internal static string BuildArtworkVariantStreamUrl(Guid variantId) =>
        $"/stream/artwork/{variantId}";

    internal sealed record ArtworkEditorEnvelope(
        [property: JsonPropertyName("entity_id")] Guid EntityId,
        [property: JsonPropertyName("slots")] IReadOnlyList<ArtworkSlotEnvelope> Slots);
    private sealed record MediaEditorContextEnvelope(
        [property: JsonPropertyName("launch_entity_id")] Guid LaunchEntityId,
        [property: JsonPropertyName("launch_entity_kind")] string LaunchEntityKind,
        [property: JsonPropertyName("media_type")] string MediaType,
        [property: JsonPropertyName("editor_mode")] string EditorMode,
        [property: JsonPropertyName("available_tabs")] IReadOnlyList<string> AvailableTabs,
        [property: JsonPropertyName("content_tab_label")] string? ContentTabLabel,
        [property: JsonPropertyName("supports_file_tab")] bool SupportsFileTab,
        [property: JsonPropertyName("file_metadata_sync_status")] string FileMetadataSyncStatus,
        [property: JsonPropertyName("current_target_summary")] MediaEditorTargetSummaryEnvelope CurrentTargetSummary,
        [property: JsonPropertyName("identity_summary")] MediaEditorIdentitySummaryEnvelope IdentitySummary,
        [property: JsonPropertyName("field_lock_map")] IReadOnlyDictionary<string, bool> FieldLockMap,
        [property: JsonPropertyName("display_override_keys")] IReadOnlyList<string> DisplayOverrideKeys,
        [property: JsonPropertyName("display_overrides")] IReadOnlyDictionary<string, string> DisplayOverrides,
        [property: JsonPropertyName("initial_scope")] string InitialScope,
        [property: JsonPropertyName("scopes")] IReadOnlyList<MediaEditorScopeEnvelope> Scopes);
    private sealed record MediaEditorTargetSummaryEnvelope(
        [property: JsonPropertyName("label")] string Label,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("subtitle")] string? Subtitle);
    private sealed record MediaEditorIdentitySummaryEnvelope(
        [property: JsonPropertyName("provider_name")] string? ProviderName,
        [property: JsonPropertyName("provider_item_id")] string? ProviderItemId,
        [property: JsonPropertyName("match_source")] string? MatchSource,
        [property: JsonPropertyName("match_method")] string? MatchMethod,
        [property: JsonPropertyName("wikidata_qid")] string? WikidataQid,
        [property: JsonPropertyName("wikidata_status")] string? WikidataStatus,
        [property: JsonPropertyName("match_level")] string? MatchLevel,
        [property: JsonPropertyName("universe_name")] string? UniverseName,
        [property: JsonPropertyName("universe_qid")] string? UniverseQid,
        [property: JsonPropertyName("stage3_status")] string? Stage3Status);
    private sealed record MediaEditorScopeEnvelope(
        [property: JsonPropertyName("scope_id")] string ScopeId,
        [property: JsonPropertyName("label")] string Label,
        [property: JsonPropertyName("order")] int Order,
        [property: JsonPropertyName("field_entity_id")] Guid FieldEntityId,
        [property: JsonPropertyName("field_entity_kind")] string FieldEntityKind,
        [property: JsonPropertyName("artwork_owner_entity_id")] Guid? ArtworkOwnerEntityId,
        [property: JsonPropertyName("artwork_owner_entity_kind")] string? ArtworkOwnerEntityKind,
        [property: JsonPropertyName("display_title")] string DisplayTitle,
        [property: JsonPropertyName("display_subtitle")] string? DisplaySubtitle,
        [property: JsonPropertyName("breadcrumb_label")] string BreadcrumbLabel,
        [property: JsonPropertyName("canonical_target_group")] string CanonicalTargetGroup,
        [property: JsonPropertyName("identity_summary")] MediaEditorIdentitySummaryEnvelope IdentitySummary,
        [property: JsonPropertyName("scope_summary")] string? ScopeSummary,
        [property: JsonPropertyName("read_only_hint")] string? ReadOnlyHint,
        [property: JsonPropertyName("can_edit_fields")] bool CanEditFields,
        [property: JsonPropertyName("can_edit_artwork")] bool CanEditArtwork);
    internal sealed record ArtworkSlotEnvelope(
        [property: JsonPropertyName("asset_type")] string AssetType,
        [property: JsonPropertyName("variants")] IReadOnlyList<ArtworkVariantEnvelope> Variants);
    internal sealed record ArtworkVariantEnvelope(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("asset_type")] string AssetType,
        [property: JsonPropertyName("image_url")] string? ImageUrl,
        [property: JsonPropertyName("is_preferred")] bool IsPreferred,
        [property: JsonPropertyName("origin")] string Origin,
        [property: JsonPropertyName("provider_name")] string? ProviderName,
        [property: JsonPropertyName("can_delete")] bool CanDelete,
        [property: JsonPropertyName("created_at")] DateTimeOffset? CreatedAt);
    internal sealed record ProviderArtworkRefreshEnvelope(
        [property: JsonPropertyName("provider")] string Provider,
        [property: JsonPropertyName("provider_name")] string ProviderName,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("skipped")] bool Skipped,
        [property: JsonPropertyName("skipped_reason")] string? SkippedReason,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("media_type")] string? MediaType,
        [property: JsonPropertyName("bridge_key")] string? BridgeKey,
        [property: JsonPropertyName("bridge_id")] string? BridgeId,
        [property: JsonPropertyName("endpoint")] string? Endpoint,
        [property: JsonPropertyName("http_status_code")] int? HttpStatusCode,
        [property: JsonPropertyName("downloaded_count")] int DownloadedCount,
        [property: JsonPropertyName("updated_preferred_count")] int UpdatedPreferredCount,
        [property: JsonPropertyName("stored_variant_counts")] IReadOnlyDictionary<string, int> StoredVariantCounts,
        [property: JsonPropertyName("diagnostics")] IReadOnlyList<string> Diagnostics,
        [property: JsonPropertyName("last_checked_at")] DateTimeOffset LastCheckedAt);
    internal sealed record ProviderArtworkRefreshTarget(
        Guid? RepresentativeAssetId,
        string? WorkQid,
        ProviderArtworkRefreshEnvelope? Skipped)
    {
        public static ProviderArtworkRefreshTarget Skip(ProviderArtworkRefreshEnvelope envelope) =>
            new(null, null, envelope);
    }
    private sealed record EditorScopeContext(
        Guid LaunchEntityId,
        string LaunchEntityKind,
        string MediaType,
        string EditorMode,
        IReadOnlyList<string> AvailableTabs,
        string? ContentTabLabel,
        bool SupportsFileTab,
        string FileMetadataSyncStatus,
        MediaEditorTargetSummaryEnvelope CurrentTargetSummary,
        MediaEditorIdentitySummaryEnvelope IdentitySummary,
        IReadOnlyDictionary<string, bool> FieldLockMap,
        IReadOnlyList<string> DisplayOverrideKeys,
        IReadOnlyDictionary<string, string> DisplayOverrides,
        string InitialScope,
        IReadOnlyList<EditorScopeResolution> Scopes,
        IReadOnlyDictionary<Guid, MediaEditorIdentitySummaryEnvelope> ScopeIdentitySummaries);
    internal sealed record EditorScopeResolution(
        string ScopeId,
        string Label,
        int Order,
        Guid FieldEntityId,
        string FieldEntityKind,
        Guid? ArtworkOwnerEntityId,
        string? ArtworkOwnerEntityKind,
        string DisplayTitle,
        string? DisplaySubtitle,
        string BreadcrumbLabel,
        string CanonicalTargetGroup,
        string? ScopeSummary,
        string? ReadOnlyHint,
        bool CanEditFields,
        bool CanEditArtwork,
        string MediaType,
        string? ArtworkFolderPath,
        string? RepresentativeMediaFilePath);

    /// <summary>
    /// Builds a flat dictionary of all extracted fields from a search result item.
    /// Used by the fan-out search response to populate the diff grid.
    /// </summary>
    private static Dictionary<string, string> BuildRawFields(
        MediaEngine.Providers.Models.SearchResultItem item)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(item.Title))
            fields[MetadataFieldConstants.Title] = item.Title;
        if (!string.IsNullOrEmpty(item.Author))
            fields[MetadataFieldConstants.Author] = item.Author;
        if (!string.IsNullOrEmpty(item.Description))
            fields[MetadataFieldConstants.Description] = item.Description;
        if (!string.IsNullOrEmpty(item.Year))
            fields[MetadataFieldConstants.Year] = item.Year;
        if (!string.IsNullOrEmpty(item.ThumbnailUrl))
            fields[MetadataFieldConstants.Cover] = item.ThumbnailUrl;
        if (!string.IsNullOrEmpty(item.ProviderItemId))
            fields["provider_item_id"] = item.ProviderItemId;
        if (item.ExtraFields is { Count: > 0 })
        {
            foreach (var (key, value) in item.ExtraFields)
            {
                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                    fields[key] = value;
            }
        }

        return fields;
    }
}
