using System.Text.Json.Nodes;
using System.Text.Json;
using System.Globalization;
using System.Text.Json.Serialization;
using Dapper;
using MediaEngine.Api.Models;
using MediaEngine.Api.Security;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Helpers;
using MediaEngine.Providers.Models;
using MediaEngine.Storage.Contracts;

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
            IMetadataClaimRepository claimRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            // First try direct lookup — covers assets/editions queried by their own ID.
            var claims = await claimRepo.GetByEntityAsync(entityId, ct);

            // If no claims found, entityId might be a Work ID. Look up all asset IDs
            // that belong to editions of this work and return their claims combined.
            if (claims.Count == 0)
            {
                using var conn = db.CreateConnection();
                var assetIds = conn.Query<string>("""
                    SELECT ma.id FROM media_assets ma
                    JOIN editions e ON ma.edition_id = e.id
                    WHERE e.work_id = @WorkId
                    """, new { WorkId = entityId.ToString() }).ToList();

                if (assetIds.Count > 0)
                {
                    var allClaims = new List<Domain.Entities.MetadataClaim>();
                    foreach (var assetId in assetIds)
                    {
                        if (Guid.TryParse(assetId, out var assetGuid))
                        {
                            var assetClaims = await claimRepo.GetByEntityAsync(assetGuid, ct);
                            allClaims.AddRange(assetClaims);
                        }
                    }
                    // Deduplicate by (entity_id, claim_key, claim_value) — keep one per unique combination
                    claims = allClaims
                        .GroupBy(c => (c.ClaimKey, c.ClaimValue, c.ProviderId))
                        .Select(g => g.OrderByDescending(c => c.Confidence).First())
                        .OrderBy(c => c.ClaimedAt)
                        .ToList();
                }
            }

            var dtos = claims.Select(ClaimDto.FromDomain).ToList();
            return Results.Ok(dtos);
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
            IDatabaseConnection db,
            ITransactionJournal journal,
            IEventPublisher publisher,
            CancellationToken ct) =>
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(request.ClaimKey))
                return Results.BadRequest("claim_key must not be empty.");
            if (string.IsNullOrWhiteSpace(request.ChosenValue))
                return Results.BadRequest("chosen_value must not be empty.");
            if (!UserLockableFields.Contains(request.ClaimKey))
                return Results.BadRequest(
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
            using var conn = db.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO canonical_values (entity_id, key, value, last_scored_at, is_conflicted)
                VALUES (@entity_id, @key, @value, @last_scored_at, 0)
                ON CONFLICT(entity_id, key) DO UPDATE SET
                    value          = excluded.value,
                    last_scored_at = excluded.last_scored_at,
                    is_conflicted  = 0;
                """;
            cmd.Parameters.AddWithValue("@entity_id",      request.EntityId.ToString());
            cmd.Parameters.AddWithValue("@key",            request.ClaimKey);
            cmd.Parameters.AddWithValue("@value",          request.ChosenValue);
            cmd.Parameters.AddWithValue("@last_scored_at", lockedAt.ToString("O"));
            cmd.ExecuteNonQuery();

            // 3. Audit trail.
            journal.Log("CLAIM_USER_LOCKED", "MetadataClaim", request.EntityId.ToString());

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
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAdminOrCurator();

        // -- PATCH /metadata/resolve (legacy) ---------------------------------
        group.MapMethods("/resolve", ["PATCH"], (
            ResolveRequest request,
            IDatabaseConnection db,
            ITransactionJournal journal,
            CancellationToken ct) =>
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(request.ClaimKey))
                return Results.BadRequest("claim_key must not be empty.");

            if (string.IsNullOrWhiteSpace(request.ChosenValue))
                return Results.BadRequest("chosen_value must not be empty.");

            var resolvedAt = DateTimeOffset.UtcNow;

            using var conn = db.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO canonical_values (entity_id, key, value, last_scored_at, is_conflicted)
                VALUES (@entity_id, @key, @value, @last_scored_at, 0)
                ON CONFLICT(entity_id, key) DO UPDATE SET
                    value          = excluded.value,
                    last_scored_at = excluded.last_scored_at,
                    is_conflicted  = 0;
                """;
            cmd.Parameters.AddWithValue("@entity_id",      request.EntityId.ToString());
            cmd.Parameters.AddWithValue("@key",            request.ClaimKey);
            cmd.Parameters.AddWithValue("@value",          request.ChosenValue);
            cmd.Parameters.AddWithValue("@last_scored_at", resolvedAt.ToString("O"));
            cmd.ExecuteNonQuery();

            journal.Log(
                "CANONICAL_VALUE_MANUAL_RESOLVE",
                "CanonicalValue",
                request.EntityId.ToString());

            return Results.Ok(new ResolveResponse
            {
                EntityId    = request.EntityId,
                ClaimKey    = request.ClaimKey,
                ChosenValue = request.ChosenValue,
                ResolvedAt  = resolvedAt,
            });
        })
        .WithName("ResolveMetadataConflict")
        .WithSummary("Manually override a metadata canonical value, locking in the chosen value.")
        .Produces<ResolveResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
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
                return Results.BadRequest("provider_name is required.");

            if (string.IsNullOrWhiteSpace(request.Query))
                return Results.BadRequest("query is required.");

            // Find the named provider.
            var provider = providers.FirstOrDefault(
                p => string.Equals(p.Name, request.ProviderName, StringComparison.OrdinalIgnoreCase));

            if (provider is null)
                return Results.NotFound($"Provider '{request.ProviderName}' not found.");

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
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
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
                return Results.BadRequest("At least one field override is required.");

            var now = DateTimeOffset.UtcNow;
            var updatedKeys = new List<string>();

            var claims = new List<MetadataClaim>();
            var canonicals = new List<CanonicalValue>();

            // Validate all keys before persisting anything.
            var rejectedKeys = request.Fields.Keys
                .Where(k => !string.IsNullOrWhiteSpace(k) && !UserLockableFields.Contains(k))
                .ToList();
            if (rejectedKeys.Count > 0)
                return Results.BadRequest(
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
                return Results.BadRequest("No valid field overrides provided.");

            // 3. Log to activity ledger.
            await activityRepo.LogAsync(new SystemActivityEntry
            {
                ActionType = SystemActionType.MetadataManualOverride,
                EntityId   = entityId,
                Detail     = $"Manual override: {updatedKeys.Count} field(s) — {string.Join(", ", updatedKeys)}.",
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
                // Non-fatal — write-back failure should not prevent override success.
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
        .Produces(StatusCodes.Status400BadRequest)
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
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.MediaType))
                return Results.BadRequest("media_type is required.");

            // Validate the media type string parses to a known MediaType enum value.
            if (!Enum.TryParse<Domain.Enums.MediaType>(request.MediaType, ignoreCase: true, out var newMediaType)
                || newMediaType == Domain.Enums.MediaType.Unknown)
            {
                return Results.BadRequest($"Invalid media type: {request.MediaType}");
            }

            var now = DateTimeOffset.UtcNow;

            // 1. Create a user-locked media_type claim at confidence 1.0.
            var claim = new MetadataClaim
            {
                Id           = Guid.NewGuid(),
                EntityId     = entityId,
                ProviderId   = WellKnownProviders.UserManual,
                ClaimKey     = MetadataFieldConstants.MediaTypeField,
                ClaimValue   = newMediaType.ToString(),
                ClaimedAt    = now,
                IsUserLocked = true,
            };
            await claimRepo.InsertBatchAsync([claim], ct);

            // 2. Upsert the canonical media_type value.
            await canonicalRepo.UpsertBatchAsync([new CanonicalValue
            {
                EntityId     = entityId,
                Key          = MetadataFieldConstants.MediaTypeField,
                Value        = newMediaType.ToString(),
                LastScoredAt = now,
                IsConflicted = false,
            }], ct);

            // 3. Resolve any pending AmbiguousMediaType review items for this entity.
            bool reviewResolved = false;
            var reviews = await reviewRepo.GetByEntityAsync(entityId, ct);
            foreach (var review in reviews.Where(r =>
                r.Status == ReviewStatus.Pending &&
                r.Trigger == ReviewTrigger.AmbiguousMediaType))
            {
                await reviewRepo.UpdateStatusAsync(review.Id, ReviewStatus.Resolved, "user", ct);
                reviewResolved = true;
            }

            // 4. Re-trigger hydration with the correct media type.
            var canonicals = await canonicalRepo.GetByEntityAsync(entityId, ct);
            var hints = canonicals.ToDictionary(
                c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase);

            await pipeline.EnqueueAsync(new HarvestRequest
            {
                EntityId   = entityId,
                EntityType = EntityType.MediaAsset,
                MediaType  = newMediaType,
                Hints      = hints,
            }, ct);

            // 5. Log activity.
            await activityRepo.LogAsync(new SystemActivityEntry
            {
                ActionType = SystemActionType.MetadataRefreshed,
                EntityId   = entityId,
                EntityType = "MediaAsset",
                Detail     = $"Media type reclassified to {newMediaType} by user.",
            }, ct);

            // 6. Broadcast events.
            await publisher.PublishAsync(SignalREvents.MetadataHarvested, new
            {
                entity_id  = entityId,
                media_type = newMediaType.ToString(),
            }, ct);

            if (reviewResolved)
            {
                await publisher.PublishAsync(SignalREvents.ReviewItemResolved, new
                {
                    entity_id = entityId,
                    status    = "Resolved",
                }, ct);
            }

            return Results.Ok(new ReclassifyResponse
            {
                EntityId        = entityId,
                NewMediaType    = newMediaType.ToString(),
                ReclassifiedAt  = now,
                ReviewResolved  = reviewResolved,
            });
        })
        .WithName("ReclassifyMediaType")
        .WithSummary("Reclassify a media asset to a different media type. Creates a user-locked claim and re-triggers hydration.")
        .Produces<ReclassifyResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAdminOrCurator();

        // -- GET /metadata/{entityId}/editor-context -------------------------
        group.MapGet("/{entityId:guid}/editor-context", async (
            Guid entityId,
            ICanonicalValueRepository canonicalRepo,
            ILibraryItemRepository libraryItemRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            var context = await ResolveEditorScopeContextAsync(entityId, canonicalRepo, libraryItemRepo, db, ct);
            if (context is null)
                return Results.NotFound($"Editor context for {entityId} not found.");

            return Results.Ok(new MediaEditorContextEnvelope(
                context.LaunchEntityId,
                context.LaunchEntityKind,
                context.MediaType,
                context.EditorMode,
                context.AvailableTabs,
                context.ContentTabLabel,
                context.SupportsFileTab,
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
                    scope.ScopeSummary,
                    scope.ReadOnlyHint,
                    scope.CanEditFields,
                    scope.CanEditArtwork))
                    .ToList()));
        })
        .WithName("GetMediaEditorContext")
        .WithSummary("Resolve scope-aware edit panel context for a launch entity.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        MapMediaEditorNavigatorEndpoints(group);

        // -- GET /metadata/{entityId}/artwork/{scopeId} ---------------------
        group.MapGet("/{entityId:guid}/artwork/{scopeId}", async (
            Guid entityId,
            string scopeId,
            ICanonicalValueRepository canonicalRepo,
            ILibraryItemRepository libraryItemRepo,
            IEntityAssetRepository entityAssetRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            var context = await ResolveEditorScopeContextAsync(entityId, canonicalRepo, libraryItemRepo, db, ct);
            if (context is null)
                return Results.NotFound($"Editor context for {entityId} not found.");

            var scope = context.Scopes.FirstOrDefault(candidate =>
                string.Equals(candidate.ScopeId, scopeId, StringComparison.OrdinalIgnoreCase));
            if (scope is null)
                return Results.NotFound($"Scope '{scopeId}' was not found for {entityId}.");

            var artwork = await BuildScopedArtworkEnvelopeAsync(scope, entityAssetRepo, canonicalRepo, libraryItemRepo, ct);
            return Results.Ok(artwork);
        })
        .WithName("GetScopedArtworkEditor")
        .WithSummary("Return grouped artwork variants for one editor scope.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        // -- GET /metadata/{entityId}/artwork --------------------------------
        group.MapGet("/{entityId:guid}/artwork", async (
            Guid entityId,
            IMediaAssetRepository assetRepo,
            IWorkRepository workRepo,
            IEntityAssetRepository entityAssetRepo,
            ICanonicalValueRepository canonicalRepo,
            ILibraryItemRepository libraryItemRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            var context = await ResolveArtworkContextAsync(entityId, db, ct);
            var detail = context.WorkId is Guid workId
                ? await libraryItemRepo.GetDetailAsync(workId, ct)
                : null;

            var assets = new List<EntityAsset>();
            foreach (var artworkEntityId in context.ArtworkEntityIds)
            {
                assets.AddRange(await entityAssetRepo.GetByEntityAsync(artworkEntityId.ToString(), null, ct));
            }

            var canonicalSources = new List<Guid>();
            AddCanonicalSource(canonicalSources, context.WorkId);
            AddCanonicalSource(canonicalSources, context.RootWorkId);
            AddCanonicalSource(canonicalSources, context.PrimaryAssetId);
            AddCanonicalSource(canonicalSources, context.RootPrimaryAssetId);

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
                    .GroupBy(BuildArtworkVariantIdentity, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group
                        .OrderByDescending(asset => asset.IsPreferred)
                        .ThenByDescending(asset => asset.CreatedAt)
                        .First())
                    .OrderByDescending(asset => asset.IsPreferred)
                    .ThenByDescending(asset => asset.CreatedAt)
                    .Select(MapArtworkVariant)
                    .ToList();

                var preferredUrl = GetArtworkCanonicalValue(canonicals, assetType)
                                   ?? GetArtworkDetailUrl(detail, assetType);

                if (!string.IsNullOrWhiteSpace(preferredUrl)
                    && !variants.Any(variant => string.Equals(variant.ImageUrl, preferredUrl, StringComparison.OrdinalIgnoreCase)))
                {
                    variants.Insert(0, new ArtworkVariantEnvelope(
                        Guid.Empty,
                        assetType,
                        preferredUrl,
                        true,
                        InferSyntheticArtworkOrigin(canonicals, assetType, detail?.ArtworkSource),
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
            ICanonicalValueRepository canonicalRepo,
            IEntityAssetRepository entityAssetRepo,
            IAssetExportService assetExportService,
            ISystemActivityRepository activityRepo,
            IDatabaseConnection db,
            AssetPathService assetPathService,
            HttpRequest httpRequest,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var coverLogger = loggerFactory.CreateLogger("CoverUpload");

            if (!httpRequest.HasFormContentType)
                return Results.BadRequest("Expected multipart form data.");

            var form = await httpRequest.ReadFormAsync(ct);
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest("No file provided.");

            var normalizedAssetType = "CoverArt";
            if (!IsArtworkUploadAllowed(file.ContentType, normalizedAssetType))
                return Results.BadRequest("Only JPEG and PNG images are accepted.");

            var context = await ResolveArtworkContextAsync(entityId, db, ct);
            var targetEntityId = context.RootWorkId ?? context.WorkId;
            if (targetEntityId is null || targetEntityId == Guid.Empty)
                return Results.NotFound($"Asset {entityId} not found.");

            var variantId = Guid.NewGuid();
            var localPath = BuildArtworkUploadPath(
                assetPathService,
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
            await SyncArtworkCanonicalAsync(targetEntityId.Value, normalizedAssetType, storedAsset, canonicalRepo, entityAssetRepo, ct);
            await assetExportService.ReconcileArtworkAsync(storedAsset.EntityId, storedAsset.EntityType, storedAsset.AssetTypeValue, ct);

            coverLogger.LogInformation("Cover uploaded for {EntityId} ? {Path}", entityId, localPath);

            await activityRepo.LogAsync(new SystemActivityEntry
            {
                ActionType = SystemActionType.CoverArtSaved,
                EntityId   = entityId,
                EntityType = "MediaAsset",
                Detail     = "Cover art uploaded manually",
            }, ct);

            return Results.Ok(new
            {
                entity_id = entityId,
                asset_type = normalizedAssetType,
                variant_id = storedAsset.Id,
                image_url = storedAsset.ImageUrl,
            });
        })
        .WithName("UploadCover")
        .WithSummary("Upload cover art for a media asset.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
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
            IDatabaseConnection db,
            AssetPathService assetPathService,
            HttpRequest httpRequest,
            CancellationToken ct) =>
        {
            var normalizedAssetType = NormalizeUploadedArtworkType(assetType);
            if (normalizedAssetType is null)
                return Results.BadRequest("Artwork type is not supported for scoped upload.");

            if (!httpRequest.HasFormContentType)
                return Results.BadRequest("Expected multipart form data.");

            var context = await ResolveEditorScopeContextAsync(entityId, canonicalRepo, libraryItemRepo, db, ct);
            if (context is null)
                return Results.NotFound($"Editor context for {entityId} not found.");

            var scope = context.Scopes.FirstOrDefault(candidate =>
                string.Equals(candidate.ScopeId, scopeId, StringComparison.OrdinalIgnoreCase));
            if (scope is null)
                return Results.NotFound($"Scope '{scopeId}' was not found for {entityId}.");

            if (!scope.CanEditArtwork || scope.ArtworkOwnerEntityId is null || string.IsNullOrWhiteSpace(scope.ArtworkOwnerEntityKind))
                return Results.BadRequest($"Scope '{scope.Label}' does not accept artwork uploads.");

            var allowedSlots = GetScopedArtworkSlots(scope.MediaType, scope.ScopeId);
            if (!allowedSlots.Contains(normalizedAssetType, StringComparer.OrdinalIgnoreCase))
                return Results.BadRequest($"{normalizedAssetType} is not valid for the {scope.Label} scope.");

            var form = await httpRequest.ReadFormAsync(ct);
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest("No file provided.");

            if (!IsArtworkUploadAllowed(file.ContentType, normalizedAssetType))
                return Results.BadRequest(normalizedAssetType == "Logo"
                    ? "Logo uploads must be PNG images."
                    : "Only JPEG and PNG images are accepted.");

            var variantId = Guid.NewGuid();
            var localPath = BuildScopedArtworkUploadPath(assetPathService, scope, normalizedAssetType, variantId, file.ContentType);
            if (string.IsNullOrWhiteSpace(localPath))
                return Results.NotFound($"Could not resolve an artwork folder for the {scope.Label} scope.");

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
            await SyncArtworkCanonicalAsync(scope.ArtworkOwnerEntityId.Value, normalizedAssetType, storedAsset, canonicalRepo, entityAssetRepo, ct);
            await assetExportService.ReconcileArtworkAsync(storedAsset.EntityId, storedAsset.EntityType, storedAsset.AssetTypeValue, ct);

            return Results.Ok(new
            {
                entity_id = entityId,
                scope_id = scope.ScopeId,
                owner_entity_id = scope.ArtworkOwnerEntityId,
                asset_type = normalizedAssetType,
                variant_id = storedAsset.Id,
                image_url = storedAsset.ImageUrl,
            });
        })
        .WithName("UploadScopedArtwork")
        .WithSummary("Upload a new artwork variant for a specific editor scope owner.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
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
            IDatabaseConnection db,
            AssetPathService assetPathService,
            IHttpClientFactory httpFactory,
            CancellationToken ct) =>
        {
            var normalizedAssetType = NormalizeUploadedArtworkType(assetType);
            if (normalizedAssetType is null)
                return Results.BadRequest("Artwork type is not supported for scoped download.");

            if (string.IsNullOrWhiteSpace(request.ImageUrl))
                return Results.BadRequest("image_url is required.");

            var context = await ResolveEditorScopeContextAsync(entityId, canonicalRepo, libraryItemRepo, db, ct);
            if (context is null)
                return Results.NotFound($"Editor context for {entityId} not found.");

            var scope = context.Scopes.FirstOrDefault(candidate =>
                string.Equals(candidate.ScopeId, scopeId, StringComparison.OrdinalIgnoreCase));
            if (scope is null)
                return Results.NotFound($"Scope '{scopeId}' was not found for {entityId}.");

            if (!scope.CanEditArtwork || scope.ArtworkOwnerEntityId is null || string.IsNullOrWhiteSpace(scope.ArtworkOwnerEntityKind))
                return Results.BadRequest($"Scope '{scope.Label}' does not accept artwork downloads.");

            var allowedSlots = GetScopedArtworkSlots(scope.MediaType, scope.ScopeId);
            if (!allowedSlots.Contains(normalizedAssetType, StringComparer.OrdinalIgnoreCase))
                return Results.BadRequest($"{normalizedAssetType} is not valid for the {scope.Label} scope.");

            using var client = httpFactory.CreateClient("cover_download");
            using var response = await client.GetAsync(request.ImageUrl, ct);
            if (!response.IsSuccessStatusCode)
                return Results.BadRequest($"Failed to download image: {(int)response.StatusCode} {response.ReasonPhrase}");

            var imageBytes = await response.Content.ReadAsByteArrayAsync(ct);
            if (imageBytes.Length == 0)
                return Results.BadRequest("Downloaded image is empty.");

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (!IsArtworkUploadAllowed(contentType, normalizedAssetType))
                return Results.BadRequest(normalizedAssetType == "Logo"
                    ? "Logo uploads must be PNG images."
                    : "Only JPEG and PNG images are accepted.");

            var variantId = Guid.NewGuid();
            var localPath = BuildScopedArtworkUploadPath(assetPathService, scope, normalizedAssetType, variantId, contentType);
            if (string.IsNullOrWhiteSpace(localPath))
                return Results.NotFound($"Could not resolve an artwork folder for the {scope.Label} scope.");

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
            await SyncArtworkCanonicalAsync(scope.ArtworkOwnerEntityId.Value, normalizedAssetType, storedAsset, canonicalRepo, entityAssetRepo, ct);
            await assetExportService.ReconcileArtworkAsync(storedAsset.EntityId, storedAsset.EntityType, storedAsset.AssetTypeValue, ct);

            return Results.Ok(new
            {
                entity_id = entityId,
                scope_id = scope.ScopeId,
                owner_entity_id = scope.ArtworkOwnerEntityId,
                asset_type = normalizedAssetType,
                variant_id = storedAsset.Id,
                image_url = storedAsset.ImageUrl,
            });
        })
        .WithName("UploadScopedArtworkFromUrl")
        .WithSummary("Download a new artwork variant from a URL for a specific editor scope owner.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        // -- POST /metadata/{entityId}/artwork/{assetType} -------------------
        group.MapPost("/{entityId:guid}/artwork/{assetType}", async (
            Guid entityId,
            string assetType,
            ICanonicalValueRepository canonicalRepo,
            IEntityAssetRepository entityAssetRepo,
            IAssetExportService assetExportService,
            IDatabaseConnection db,
            AssetPathService assetPathService,
            HttpRequest httpRequest,
            CancellationToken ct) =>
        {
            var normalizedAssetType = NormalizeUploadedArtworkType(assetType);
            if (normalizedAssetType is null)
                return Results.BadRequest("Artwork type must be CoverArt, SquareArt, Background, Banner, or Logo.");

            if (!httpRequest.HasFormContentType)
                return Results.BadRequest("Expected multipart form data.");

            var form = await httpRequest.ReadFormAsync(ct);
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest("No file provided.");

            if (!IsArtworkUploadAllowed(file.ContentType, normalizedAssetType))
                return Results.BadRequest(normalizedAssetType == "Logo"
                    ? "Logo uploads must be PNG images."
                    : "Only JPEG and PNG images are accepted.");

            var context = await ResolveArtworkContextAsync(entityId, db, ct);
            var targetEntityId = context.RootWorkId ?? context.WorkId;
            if (targetEntityId is null || targetEntityId == Guid.Empty)
                return Results.NotFound($"Asset {entityId} not found.");

            var variantId = Guid.NewGuid();
            var localPath = BuildArtworkUploadPath(
                assetPathService,
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
            await SyncArtworkCanonicalAsync(targetEntityId.Value, normalizedAssetType, storedAsset, canonicalRepo, entityAssetRepo, ct);
            await assetExportService.ReconcileArtworkAsync(storedAsset.EntityId, storedAsset.EntityType, storedAsset.AssetTypeValue, ct);

            return Results.Ok(new
            {
                entity_id = entityId,
                asset_type = normalizedAssetType,
                variant_id = storedAsset.Id,
                image_url = storedAsset.ImageUrl,
            });
        })
        .WithName("UploadEntityArtwork")
        .WithSummary("Upload a new artwork variant for a media asset.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator()
        .DisableAntiforgery();

        // -- PUT /metadata/artwork/{variantId}/preferred ---------------------
        group.MapPut("/artwork/{variantId:guid}/preferred", async (
            Guid variantId,
            IEntityAssetRepository entityAssetRepo,
            IAssetExportService assetExportService,
            ICanonicalValueRepository canonicalRepo,
            CancellationToken ct) =>
        {
            var target = await entityAssetRepo.FindByIdAsync(variantId, ct);
            if (target is null)
                return Results.NotFound($"Artwork variant {variantId} not found.");

            await entityAssetRepo.SetPreferredAsync(variantId, ct);
            target = await entityAssetRepo.FindByIdAsync(variantId, ct);
            if (target is null)
                return Results.NotFound($"Artwork variant {variantId} not found.");

            await SyncArtworkCanonicalAsync(
                Guid.Parse(target.EntityId),
                target.AssetTypeValue,
                target,
                canonicalRepo,
                entityAssetRepo,
                ct);
            await assetExportService.ReconcileArtworkAsync(target.EntityId, target.EntityType, target.AssetTypeValue, ct);

            return Results.Ok(new
            {
                variant_id = variantId,
                asset_type = target.AssetTypeValue,
                image_url = target.ImageUrl,
            });
        })
        .WithName("SetPreferredArtwork")
        .WithSummary("Mark an artwork variant as preferred for its slot.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        // -- DELETE /metadata/artwork/{variantId} ----------------------------
        group.MapDelete("/artwork/{variantId:guid}", async (
            Guid variantId,
            IEntityAssetRepository entityAssetRepo,
            IAssetExportService assetExportService,
            ICanonicalValueRepository canonicalRepo,
            CancellationToken ct) =>
        {
            var target = await entityAssetRepo.FindByIdAsync(variantId, ct);
            if (target is null)
                return Results.NotFound($"Artwork variant {variantId} not found.");

            if (!string.Equals(target.SourceProvider, "user_upload", StringComparison.OrdinalIgnoreCase)
                && !target.IsUserOverride)
            {
                return Results.BadRequest("Only uploaded artwork variants can be deleted.");
            }

            var entityId = Guid.Parse(target.EntityId);
            var siblings = await entityAssetRepo.GetByEntityAsync(target.EntityId, target.AssetTypeValue, ct);
            var wasPreferred = target.IsPreferred;

            if (!string.IsNullOrWhiteSpace(target.LocalImagePath))
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

            await SyncArtworkCanonicalAsync(entityId, target.AssetTypeValue, nextPreferred, canonicalRepo, entityAssetRepo, ct);
            if (nextPreferred is not null)
                await assetExportService.ReconcileArtworkAsync(nextPreferred.EntityId, nextPreferred.EntityType, nextPreferred.AssetTypeValue, ct);
            else
                await assetExportService.ClearArtworkExportAsync(target.EntityId, target.EntityType, target.AssetTypeValue, ct);

            return Results.Ok(new
            {
                variant_id = variantId,
                asset_type = target.AssetTypeValue,
                preferred_variant_id = nextPreferred?.Id,
            });
        })
        .WithName("DeleteArtworkVariant")
        .WithSummary("Delete an uploaded artwork variant.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
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
            IStorageManifest configLoader,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(isbn))
                return Results.BadRequest(new { error = "Provide ?title= or ?isbn= query parameter." });

            var provConfigs = ((Storage.ConfigurationDirectoryLoader)configLoader).LoadAllProviders();
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
                return Results.BadRequest("query is required.");

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
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAdminOrCurator();

        // -- GET /metadata/{entityId}/search-cache -------------------------
        group.MapGet("/{entityId:guid}/search-cache", async (
            Guid entityId,
            ISearchResultsCacheRepository cache) =>
        {
            var json = await cache.FindAsync(entityId, maxAgeDays: 30);
            return json is not null
                ? Results.Ok(new { results_json = json })
                : Results.NotFound();
        })
        .WithName("GetSearchResultsCache")
        .WithSummary("Retrieve cached fan-out search results for an entity (30-day TTL)")
        .Produces<object>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        // -- PUT /metadata/{entityId}/search-cache -------------------------
        group.MapPut("/{entityId:guid}/search-cache", async (
            Guid entityId,
            SearchCacheUpsertRequest body,
            ISearchResultsCacheRepository cache) =>
        {
            if (string.IsNullOrEmpty(body.ResultsJson))
                return Results.BadRequest("results_json is required");
            await cache.UpsertAsync(entityId, body.ResultsJson);
            return Results.NoContent();
        })
        .WithName("PutSearchResultsCache")
        .WithSummary("Cache fan-out search results for an entity")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status400BadRequest);

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
                return Results.NotFound($"No canonical values found for entity {entityId}.");

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
        .Produces(StatusCodes.Status404NotFound)
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
            ICanonicalValueRepository canonicalRepo,
            IEntityAssetRepository entityAssetRepo,
            IAssetExportService assetExportService,
            AssetPathService assetPathService,
            IHttpClientFactory httpFactory,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("CoverFromUrl");

            if (string.IsNullOrWhiteSpace(request.ImageUrl))
                return Results.BadRequest("image_url is required.");

            var asset = await assetRepo.FindByIdAsync(entityId, ct);
            if (asset is null)
                return Results.NotFound($"Media asset {entityId} not found.");

            var lineage = await workRepo.GetLineageByAssetAsync(entityId, ct);
            var ownerEntityId = lineage?.TargetForParentScope ?? entityId;

            try
            {
                using var client = httpFactory.CreateClient("cover_download");
                using var response = await client.GetAsync(request.ImageUrl, ct);
                response.EnsureSuccessStatusCode();

                var imageBytes = await response.Content.ReadAsByteArrayAsync(ct);

                if (imageBytes.Length == 0)
                    return Results.BadRequest("Downloaded image is empty.");

                var contentType = response.Content.Headers.ContentType?.MediaType;
                if (!IsArtworkUploadAllowed(contentType, "CoverArt"))
                    return Results.BadRequest("Only JPEG and PNG images are accepted.");

                var variantId = Guid.NewGuid();
                var coverPath = BuildArtworkUploadPath(assetPathService, "Work", ownerEntityId, "CoverArt", variantId, contentType);
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
                await SyncArtworkCanonicalAsync(ownerEntityId, "CoverArt", storedAsset, canonicalRepo, entityAssetRepo, ct);
                await assetExportService.ReconcileArtworkAsync(storedAsset.EntityId, storedAsset.EntityType, storedAsset.AssetTypeValue, ct);

                return Results.Ok(new
                {
                    entity_id      = entityId,
                    variant_id     = storedAsset.Id,
                    cover_path     = coverPath,
                    primary_hex    = storedAsset.PrimaryHex,
                    secondary_hex  = storedAsset.SecondaryHex,
                    accent_hex     = storedAsset.AccentHex,
                });
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "Failed to download cover from URL for entity {Id}", entityId);
                return Results.BadRequest($"Failed to download image: {ex.Message}");
            }
        })
        .WithName("CoverFromUrl")
        .WithSummary("Download cover art from a URL and rebuild measured artwork metadata. Curator+.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
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
        .Produces<Dictionary<string, LabelResolveEntry>>(StatusCodes.Status200OK);

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
                return Results.BadRequest(new { error = "qid must be a valid Wikidata QID starting with 'Q'." });

            if (reconciler is null)
                return Results.Ok(new { qid, label = (string?)null, aliases = Array.Empty<string>() });

            try
            {
                var entities = await reconciler.GetEntitiesAsync([qid], "en", ct);

                if (!entities.TryGetValue(qid, out var entity))
                    return Results.Ok(new { qid, label = (string?)null, aliases = Array.Empty<string>() });

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

                return Results.Ok(new
                {
                    qid,
                    label   = resultLabel,
                    aliases = resultAliases,
                });
            }
            catch (Exception)
            {
                return Results.Ok(new { qid, label = (string?)null, aliases = Array.Empty<string>() });
            }
        })
        .WithName("GetWikidataAliases")
        .WithSummary("Returns the Wikidata label and all aliases for a given QID.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAnyRole();

        return app;
    }

    private static async Task<EditorScopeContext?> ResolveEditorScopeContextAsync(
        Guid entityId,
        ICanonicalValueRepository canonicalRepo,
        ILibraryItemRepository libraryItemRepo,
        IDatabaseConnection db,
        CancellationToken ct)
    {
        var launch = await ResolveEditorLaunchContextAsync(entityId, db, ct);
        if (launch is null)
            return null;

        var launchDetail = await libraryItemRepo.GetDetailAsync(launch.WorkId, ct);
        var launchCanonicals = await canonicalRepo.GetByEntityAsync(launch.WorkId, ct);
        var canonicalMap = launchCanonicals
            .Where(field => !string.IsNullOrWhiteSpace(field.Key))
            .GroupBy(field => field.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.LastScoredAt).First().Value,
                StringComparer.OrdinalIgnoreCase);

        var rootDetail = launch.RootWorkId != Guid.Empty && launch.RootWorkId != launch.WorkId
            ? await libraryItemRepo.GetDetailAsync(launch.RootWorkId, ct)
            : launchDetail;

        var rootCanonicals = launch.RootWorkId != Guid.Empty && launch.RootWorkId != launch.WorkId
            ? await canonicalRepo.GetByEntityAsync(launch.RootWorkId, ct)
            : launchCanonicals;

        var rootCanonicalMap = rootCanonicals
            .Where(field => !string.IsNullOrWhiteSpace(field.Key))
            .GroupBy(field => field.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.LastScoredAt).First().Value,
                StringComparer.OrdinalIgnoreCase);

        var artistOwnerId = string.Equals(launch.MediaType, "Music", StringComparison.OrdinalIgnoreCase)
            ? ResolveArtistArtworkOwnerId(
                db,
                launch.RepresentativeAssetId,
                FirstNonBlank(GetCanonicalValue(rootCanonicalMap, "artist"), GetCanonicalValue(canonicalMap, "artist")))
            : null;

        var scopes = BuildEditorScopes(launch, launchDetail, canonicalMap, rootDetail, rootCanonicalMap, artistOwnerId);
        if (scopes.Count == 0)
            return null;

        var initialScope = GetDefaultEditorScope(launch, scopes);
        var initialScopeResolution = scopes.FirstOrDefault(scope => string.Equals(scope.ScopeId, initialScope, StringComparison.OrdinalIgnoreCase))
            ?? scopes[0];
        var editorMode = IsContainerEditorMediaType(launch.MediaType) ? "container" : "singular";
        var displayOverrides = LoadDisplayOverrides(db, initialScopeResolution.FieldEntityId);

        return new EditorScopeContext(
            launch.LaunchEntityId,
            launch.LaunchEntityKind,
            launch.MediaType,
            editorMode,
            BuildEditorAvailableTabs(editorMode, launch.MediaType, initialScopeResolution.ScopeId, initialScopeResolution.CanEditArtwork, launch.RepresentativeMediaFilePath),
            BuildContentTabLabel(editorMode, launch.MediaType),
            !string.Equals(editorMode, "container", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(launch.RepresentativeMediaFilePath),
            BuildCurrentTargetSummary(initialScopeResolution),
            BuildIdentitySummary(launchDetail),
            BuildFieldLockMap(launch.MediaType, initialScopeResolution.ScopeId),
            BuildDisplayOverrideKeys(launch.MediaType),
            displayOverrides,
            initialScope,
            scopes);
    }

    private static async Task<EditorLaunchContext?> ResolveEditorLaunchContextAsync(
        Guid entityId,
        IDatabaseConnection db,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = db.CreateConnection();

        var workRow = conn.QueryFirstOrDefault<EditorLaunchWorkRow>("""
            SELECT w.id                 AS WorkId,
                   w.media_type         AS MediaType,
                   w.work_kind          AS WorkKind,
                   w.parent_work_id     AS ParentWorkId,
                   COALESCE(gp.id, p.id, w.id) AS RootWorkId
            FROM works w
            LEFT JOIN works p ON p.id = w.parent_work_id
            LEFT JOIN works gp ON gp.id = p.parent_work_id
            WHERE w.id = @entityId
            LIMIT 1;
            """, new { entityId = entityId.ToString() });

        if (workRow is not null && Guid.TryParse(workRow.WorkId, out var workId))
        {
            var representativeAsset = GetRepresentativeAssetForWorkTree(conn, workId);
            return new EditorLaunchContext(
                entityId,
                "Work",
                workId,
                TryParseGuid(workRow.ParentWorkId),
                TryParseGuid(workRow.RootWorkId) ?? workId,
                string.IsNullOrWhiteSpace(workRow.MediaType) ? "Books" : workRow.MediaType,
                string.IsNullOrWhiteSpace(workRow.WorkKind) ? "standalone" : workRow.WorkKind,
                representativeAsset?.AssetId,
                representativeAsset?.FilePath);
        }

        var assetRow = conn.QueryFirstOrDefault<EditorLaunchAssetRow>("""
            SELECT a.id             AS AssetId,
                   a.file_path_root AS FilePath,
                   w.id             AS WorkId,
                   w.media_type     AS MediaType,
                   w.work_kind      AS WorkKind,
                   w.parent_work_id AS ParentWorkId,
                   COALESCE(gp.id, p.id, w.id) AS RootWorkId
            FROM media_assets a
            INNER JOIN editions e ON e.id = a.edition_id
            INNER JOIN works w ON w.id = e.work_id
            LEFT JOIN works p ON p.id = w.parent_work_id
            LEFT JOIN works gp ON gp.id = p.parent_work_id
            WHERE a.id = @entityId
            LIMIT 1;
            """, new { entityId = entityId.ToString() });

        if (assetRow is null || !Guid.TryParse(assetRow.WorkId, out var assetWorkId))
            return null;

        return new EditorLaunchContext(
            entityId,
            "MediaAsset",
            assetWorkId,
            TryParseGuid(assetRow.ParentWorkId),
            TryParseGuid(assetRow.RootWorkId) ?? assetWorkId,
            string.IsNullOrWhiteSpace(assetRow.MediaType) ? "Books" : assetRow.MediaType,
            string.IsNullOrWhiteSpace(assetRow.WorkKind) ? "standalone" : assetRow.WorkKind,
            TryParseGuid(assetRow.AssetId),
            assetRow.FilePath);
    }

    private static bool IsContainerEditorMediaType(string? mediaType) =>
        NormalizeEditorMediaType(mediaType) is "TV" or "Music";

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
                "TV" => ["details", "episodes", "artwork", "links", "options", "history"],
                "Music" => ["details", "tracks", "artwork", "links", "options", "history"],
                _ => ["details", "links", "options", "history"],
            };
        }

        var tabs = new List<string> { "details" };
        if (canEditArtwork)
            tabs.Add("artwork");

        tabs.Add("links");
        tabs.Add("options");

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
            "Music" => ["display_title", "display_subtitle", "sort_title", "sort_album"],
            "TV" => ["display_title", "display_subtitle", "sort_title", "sort_series"],
            "Movies" => ["display_title", "display_subtitle", "sort_title"],
            "Books" => ["display_title", "display_subtitle", "sort_title", "sort_series"],
            "Audiobooks" => ["display_title", "display_subtitle", "sort_title", "sort_series"],
            "Comics" => ["display_title", "display_subtitle", "sort_title", "sort_series"],
            _ => ["display_title", "display_subtitle", "sort_title"],
        };

    private static Dictionary<string, string> LoadDisplayOverrides(IDatabaseConnection db, Guid workId)
    {
        using var conn = db.CreateConnection();
        var json = conn.QueryFirstOrDefault<string?>(
            "SELECT display_overrides_json FROM works WHERE id = @workId LIMIT 1;",
            new { workId = workId.ToString() });

        if (string.IsNullOrWhiteSpace(json))
            return new(StringComparer.OrdinalIgnoreCase);

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return parsed is null
                ? new(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static EditorAssetSample? GetRepresentativeAssetForWorkTree(
        System.Data.IDbConnection conn,
        Guid workId) =>
        conn.QueryFirstOrDefault<EditorAssetSample>("""
            WITH RECURSIVE work_tree(id, depth) AS (
                SELECT @workId, 0
                UNION ALL
                SELECT child.id, work_tree.depth + 1
                FROM works child
                INNER JOIN work_tree ON child.parent_work_id = work_tree.id
            )
            SELECT ma.id AS AssetIdValue,
                   ma.file_path_root AS FilePath
            FROM work_tree
            INNER JOIN editions e ON e.work_id = work_tree.id
            INNER JOIN media_assets ma ON ma.edition_id = e.id
            ORDER BY work_tree.depth,
                     ma.id
            LIMIT 1;
            """, new { workId = workId.ToString() });

    private static Guid? ResolveArtistArtworkOwnerId(
        IDatabaseConnection db,
        Guid? representativeAssetId,
        string? artistName)
    {
        using var conn = db.CreateConnection();

        if (representativeAssetId.HasValue)
        {
            var linkedId = conn.QueryFirstOrDefault<string>("""
                SELECT p.id
                FROM person_media_links pml
                INNER JOIN persons p ON p.id = pml.person_id
                WHERE pml.media_asset_id = @assetId
                  AND (
                        pml.role IN ('Artist', 'Performer')
                        OR EXISTS (
                            SELECT 1
                            FROM person_roles pr
                            WHERE pr.person_id = p.id
                              AND pr.role IN ('Artist', 'Performer')
                        )
                  )
                ORDER BY CASE
                    WHEN pml.role = 'Artist' THEN 0
                    WHEN pml.role = 'Performer' THEN 1
                    ELSE 2
                END,
                p.name
                LIMIT 1;
                """, new { assetId = representativeAssetId.Value.ToString() });

            if (Guid.TryParse(linkedId, out var linkedGuid))
                return linkedGuid;
        }

        if (string.IsNullOrWhiteSpace(artistName))
            return null;

        var matchedId = conn.QueryFirstOrDefault<string>("""
            SELECT p.id
            FROM persons p
            WHERE p.name = @artistName COLLATE NOCASE
            ORDER BY p.name
            LIMIT 1;
            """, new { artistName = artistName.Trim() });

        return Guid.TryParse(matchedId, out var parsedMatchedId) ? parsedMatchedId : null;
    }

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
        var itemTitle = FirstNonBlank(detail?.Title, GetCanonicalValue(canonicalMap, "title"), "Item");
        var rootTitle = FirstNonBlank(rootDetail?.Title, GetCanonicalValue(rootCanonicalMap, "title"), itemTitle);
        var itemYear = FirstNonBlank(detail?.Year, GetCanonicalValue(canonicalMap, "year"));
        var rootYear = FirstNonBlank(rootDetail?.Year, GetCanonicalValue(rootCanonicalMap, "year"), itemYear);
        var showName = FirstNonBlank(GetCanonicalValue(rootCanonicalMap, "title"), GetCanonicalValue(canonicalMap, "show_name"), detail?.ShowName, rootTitle, itemTitle);
        var seasonNumber = FirstNonBlank(GetCanonicalValue(canonicalMap, "season_number"), detail?.SeasonNumber);
        var episodeNumber = FirstNonBlank(GetCanonicalValue(canonicalMap, "episode_number"), detail?.EpisodeNumber);
        var episodeTitle = FirstNonBlank(
            detail?.EpisodeTitle,
            GetCanonicalValue(canonicalMap, "episode_title"),
            GetCanonicalValue(canonicalMap, "title"),
            itemTitle,
            "Episode");
        var artistName = FirstNonBlank(
            GetCanonicalValue(rootCanonicalMap, "artist"),
            GetCanonicalValue(canonicalMap, "artist"),
            rootDetail?.Author,
            detail?.Author,
            itemTitle);
        var albumName = FirstNonBlank(
            GetCanonicalValue(canonicalMap, "album"),
            GetCanonicalValue(rootCanonicalMap, "album"),
            rootTitle,
            itemTitle,
            "Album");
        var seriesName = FirstNonBlank(GetCanonicalValue(canonicalMap, "series"), rootTitle, detail?.Series);
        var seasonLabel = string.IsNullOrWhiteSpace(seasonNumber) ? "Season" : $"Season {seasonNumber}";
        var episodeLabel = !string.IsNullOrWhiteSpace(episodeNumber)
            ? $"Episode {episodeNumber}"
            : "Episode";
        var rootWorkId = launch.RootWorkId == Guid.Empty ? launch.WorkId : launch.RootWorkId;
        var hasParentScope = launch.ParentWorkId.HasValue && launch.ParentWorkId.Value != Guid.Empty;
        var hasDistinctRoot = rootWorkId != Guid.Empty && rootWorkId != launch.WorkId;
        var launchWorkKind = launch.WorkKind.Trim().ToLowerInvariant();
        var isParentLaunch = string.Equals(launchWorkKind, "parent", StringComparison.OrdinalIgnoreCase);
        var containerFolder = GetContainerFolderPath(launch.RepresentativeMediaFilePath);
        var seriesFolder = GetSeriesFolderPath(launch.RepresentativeMediaFilePath);
        var seasonFolder = GetSeasonFolderPath(launch.RepresentativeMediaFilePath);
        var artistFolder = GetArtistFolderPath(launch.RepresentativeMediaFilePath);

        switch (mediaType)
        {
            case "TV":
                var isEpisodeLaunch = !string.IsNullOrWhiteSpace(episodeNumber)
                    || (hasParentScope && launch.ParentWorkId!.Value != rootWorkId);
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
                    FirstNonBlank(detail?.Director, itemYear),
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
                    FirstNonBlank(seriesName, itemYear),
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

        if (!IsContainerEditorMediaType(mediaType))
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

    private static async Task<ArtworkEditorEnvelope> BuildScopedArtworkEnvelopeAsync(
        EditorScopeResolution scope,
        IEntityAssetRepository entityAssetRepo,
        ICanonicalValueRepository canonicalRepo,
        ILibraryItemRepository libraryItemRepo,
        CancellationToken ct)
    {
        var slotTypes = GetScopedArtworkSlots(scope.MediaType, scope.ScopeId);
        if (scope.ArtworkOwnerEntityId is null || slotTypes.Count == 0)
            return new ArtworkEditorEnvelope(scope.FieldEntityId, []);

        var assets = await entityAssetRepo.GetByEntityAsync(scope.ArtworkOwnerEntityId.Value.ToString(), null, ct);
        var canonicals = await canonicalRepo.GetByEntityAsync(scope.ArtworkOwnerEntityId.Value, ct);
        var detail = string.Equals(scope.ArtworkOwnerEntityKind, "Work", StringComparison.OrdinalIgnoreCase)
            ? await libraryItemRepo.GetDetailAsync(scope.ArtworkOwnerEntityId.Value, ct)
            : null;

        var payload = slotTypes.Select(assetType =>
        {
            var variants = assets
                .Where(asset => string.Equals(asset.AssetTypeValue, assetType, StringComparison.OrdinalIgnoreCase))
                .GroupBy(BuildArtworkVariantIdentity, StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(asset => asset.IsPreferred)
                    .ThenByDescending(asset => asset.CreatedAt)
                    .First())
                .OrderByDescending(asset => asset.IsPreferred)
                .ThenByDescending(asset => asset.CreatedAt)
                .Select(MapArtworkVariant)
                .ToList();

            var preferredUrl = GetArtworkCanonicalValue(canonicals, assetType)
                               ?? GetArtworkDetailUrl(detail, assetType);

            if (!string.IsNullOrWhiteSpace(preferredUrl)
                && !variants.Any(variant => string.Equals(variant.ImageUrl, preferredUrl, StringComparison.OrdinalIgnoreCase)))
            {
                variants.Insert(0, new ArtworkVariantEnvelope(
                    Guid.Empty,
                    assetType,
                    preferredUrl,
                    true,
                    InferSyntheticArtworkOrigin(canonicals, assetType, detail?.ArtworkSource),
                    ProviderName: null,
                    CanDelete: false,
                    CreatedAt: null));
            }

            return new ArtworkSlotEnvelope(assetType, variants);
        }).ToList();

        return new ArtworkEditorEnvelope(scope.ArtworkOwnerEntityId.Value, payload);
    }

    private static IReadOnlyList<string> GetScopedArtworkSlots(string mediaType, string scopeId) =>
        (NormalizeEditorMediaType(mediaType), scopeId) switch
        {
            ("TV", "series") =>
            [
                "CoverArt",
                "SquareArt",
                "Background",
                "Banner",
                "Logo",
                "ClearArt",
            ],
            ("TV", "season") =>
            [
                "SeasonPoster",
                "SeasonThumb",
            ],
            ("Movies", "item") =>
            [
                "CoverArt",
                "SquareArt",
                "Background",
                "Banner",
                "Logo",
                "DiscArt",
                "ClearArt",
            ],
            ("TV", "episode") =>
            [
                "EpisodeStill",
            ],
            ("Music", "album") =>
            [
                "CoverArt",
                "SquareArt",
                "DiscArt",
                "ClearArt",
            ],
            ("Books", "item") or ("Audiobooks", "item") or ("Comics", "item") =>
            [
                "CoverArt",
                "SquareArt",
                "Background",
            ],
            _ => [],
        };

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

    private static string? BuildScopedArtworkUploadPath(
        AssetPathService assetPathService,
        EditorScopeResolution scope,
        string normalizedAssetType,
        Guid variantId,
        string? contentType)
    {
        if (scope.ArtworkOwnerEntityId is null || string.IsNullOrWhiteSpace(scope.ArtworkOwnerEntityKind))
            return null;

        return assetPathService.GetCentralAssetPath(
            scope.ArtworkOwnerEntityKind!,
            scope.ArtworkOwnerEntityId.Value,
            normalizedAssetType,
            variantId,
            BuildArtworkExtension(normalizedAssetType, contentType));
    }

    private static string? GetContainerFolderPath(string? mediaFilePath) =>
        string.IsNullOrWhiteSpace(mediaFilePath)
            ? null
            : Path.GetDirectoryName(mediaFilePath);

    private static string? GetSeriesFolderPath(string? mediaFilePath)
    {
        var seasonFolder = GetSeasonFolderPath(mediaFilePath);
        return string.IsNullOrWhiteSpace(seasonFolder)
            ? null
            : Path.GetDirectoryName(seasonFolder);
    }

    private static string? GetSeasonFolderPath(string? mediaFilePath) =>
        string.IsNullOrWhiteSpace(mediaFilePath)
            ? null
            : Path.GetDirectoryName(mediaFilePath);

    private static string? GetArtistFolderPath(string? mediaFilePath)
    {
        var albumFolder = GetContainerFolderPath(mediaFilePath);
        return string.IsNullOrWhiteSpace(albumFolder)
            ? null
            : Path.GetDirectoryName(albumFolder);
    }

    private static string? GetCanonicalValue(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string NormalizeEditorMediaType(string? mediaType) =>
        (mediaType ?? string.Empty).Trim() switch
        {
            "Book" => "Books",
            "Comic" => "Comics",
            "" => "Books",
            var value => value,
        };

    private static string? NormalizeUploadedArtworkType(string assetType) =>
        assetType.Trim() switch
        {
            "cover" or "Cover" or "Poster" or "poster" or "CoverArt" => "CoverArt",
            "banner" or "Banner" => "Banner",
            "square" or "Square" or "SquareArt" => "SquareArt",
            "background" or "Background" => "Background",
            "logo" or "Logo" => "Logo",
            "discart" or "disc" or "DiscArt" => "DiscArt",
            "clearart" or "clear" or "ClearArt" => "ClearArt",
            "seasonposter" or "SeasonPoster" => "SeasonPoster",
            "seasonthumb" or "SeasonThumb" => "SeasonThumb",
            "episodestill" or "EpisodeStill" or "still" or "Still" => "EpisodeStill",
            _ => null,
        };

    private static bool IsArtworkUploadAllowed(string? contentType, string normalizedAssetType)
    {
        if (string.Equals(normalizedAssetType, "Logo", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedAssetType, "DiscArt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedAssetType, "ClearArt", StringComparison.OrdinalIgnoreCase))
            return string.Equals(contentType, "image/png", StringComparison.OrdinalIgnoreCase);

        return contentType is not null && (string.Equals(contentType, "image/jpeg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(contentType, "image/jpg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(contentType, "image/png", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<ArtworkResolutionContext> ResolveArtworkContextAsync(
        Guid entityId,
        IDatabaseConnection db,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = db.CreateConnection();

        var workRow = conn.QueryFirstOrDefault<ArtworkWorkResolutionRow>("""
            SELECT w.id                              AS WorkId,
                   COALESCE(gp.id, p.id, w.id)      AS RootWorkId,
                   (
                       SELECT ma_current.id
                       FROM editions e_current
                       INNER JOIN media_assets ma_current ON ma_current.edition_id = e_current.id
                       WHERE e_current.work_id = w.id
                       ORDER BY ma_current.id
                       LIMIT 1
                   )                                 AS PrimaryAssetId,
                   (
                       SELECT ma_root.id
                       FROM editions e_root
                       INNER JOIN media_assets ma_root ON ma_root.edition_id = e_root.id
                       WHERE e_root.work_id = COALESCE(gp.id, p.id, w.id)
                       ORDER BY ma_root.id
                       LIMIT 1
                   )                                 AS RootPrimaryAssetId
            FROM works w
            LEFT JOIN works p ON p.id = w.parent_work_id
            LEFT JOIN works gp ON gp.id = p.parent_work_id
            WHERE w.id = @entityId
            LIMIT 1;
            """, new { entityId = entityId.ToString() });

        if (workRow is not null)
        {
            return BuildArtworkResolutionContext(
                entityId,
                workRow.WorkId,
                workRow.RootWorkId,
                workRow.PrimaryAssetId,
                workRow.RootPrimaryAssetId,
                GetArtworkEntityIds(conn, workRow.WorkId, workRow.RootWorkId));
        }

        var assetRow = conn.QueryFirstOrDefault<ArtworkAssetResolutionRow>("""
            SELECT a.id                         AS AssetId,
                   w.id                         AS WorkId,
                   COALESCE(gp.id, p.id, w.id) AS RootWorkId,
                   (
                       SELECT ma_root.id
                       FROM editions e_root
                       INNER JOIN media_assets ma_root ON ma_root.edition_id = e_root.id
                       WHERE e_root.work_id = COALESCE(gp.id, p.id, w.id)
                       ORDER BY ma_root.id
                       LIMIT 1
                   )                            AS RootPrimaryAssetId
            FROM media_assets a
            INNER JOIN editions e ON e.id = a.edition_id
            INNER JOIN works w ON w.id = e.work_id
            LEFT JOIN works p ON p.id = w.parent_work_id
            LEFT JOIN works gp ON gp.id = p.parent_work_id
            WHERE a.id = @entityId
            LIMIT 1;
            """, new { entityId = entityId.ToString() });

        if (assetRow is not null)
        {
            var artworkEntityIds = GetArtworkEntityIds(conn, assetRow.WorkId, assetRow.RootWorkId);
            if (!artworkEntityIds.Contains(entityId))
                artworkEntityIds.Insert(0, entityId);

            return BuildArtworkResolutionContext(
                entityId,
                assetRow.WorkId,
                assetRow.RootWorkId,
                assetRow.AssetId,
                assetRow.RootPrimaryAssetId,
                artworkEntityIds);
        }

        return new ArtworkResolutionContext(
            RequestedEntityId: entityId,
            WorkId: null,
            RootWorkId: null,
            PrimaryAssetId: null,
            RootPrimaryAssetId: null,
            ArtworkEntityIds: [entityId],
            PreferredArtworkEntityId: null);
    }

    private static ArtworkResolutionContext BuildArtworkResolutionContext(
        Guid requestedEntityId,
        string? workId,
        string? rootWorkId,
        string? primaryAssetId,
        string? rootPrimaryAssetId,
        List<Guid> artworkEntityIds)
    {
        var parsedWorkId = TryParseGuid(workId);
        var parsedRootWorkId = TryParseGuid(rootWorkId) ?? parsedWorkId;
        var parsedPrimaryAssetId = TryParseGuid(primaryAssetId);
        var parsedRootPrimaryAssetId = TryParseGuid(rootPrimaryAssetId);

        var dedupedArtworkIds = artworkEntityIds
            .Where(static id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (parsedWorkId is Guid resolvedWorkId && !dedupedArtworkIds.Contains(resolvedWorkId))
            dedupedArtworkIds.Insert(0, resolvedWorkId);

        if (parsedRootWorkId is Guid resolvedRootWorkId && !dedupedArtworkIds.Contains(resolvedRootWorkId))
            dedupedArtworkIds.Add(resolvedRootWorkId);

        return new ArtworkResolutionContext(
            RequestedEntityId: requestedEntityId,
            WorkId: parsedWorkId,
            RootWorkId: parsedRootWorkId,
            PrimaryAssetId: parsedPrimaryAssetId,
            RootPrimaryAssetId: parsedRootPrimaryAssetId,
            ArtworkEntityIds: dedupedArtworkIds,
            PreferredArtworkEntityId: parsedPrimaryAssetId ?? parsedRootPrimaryAssetId);
    }

    private static List<Guid> GetArtworkEntityIds(
        System.Data.IDbConnection conn,
        string? workId,
        string? rootWorkId)
    {
        var ids = new List<Guid>();

        AddParsedGuid(ids, workId);
        AddParsedGuid(ids, rootWorkId);

        if (string.IsNullOrWhiteSpace(workId) && string.IsNullOrWhiteSpace(rootWorkId))
            return ids;

        var assetRows = conn.Query<string>("""
            SELECT DISTINCT ma.id
            FROM editions e
            INNER JOIN media_assets ma ON ma.edition_id = e.id
            WHERE e.work_id IN @workIds;
            """, new
        {
            workIds = new[]
            {
                workId,
                rootWorkId,
            }.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToArray(),
        });

        foreach (var assetId in assetRows)
            AddParsedGuid(ids, assetId);

        return ids;
    }

    private static void AddCanonicalSource(List<Guid> sources, Guid? sourceId)
    {
        if (!sourceId.HasValue || sourceId == Guid.Empty || sources.Contains(sourceId.Value))
            return;

        sources.Add(sourceId.Value);
    }

    private static string? GetArtworkDetailUrl(LibraryItemDetail? detail, string assetType) =>
        assetType switch
        {
            "CoverArt" => detail?.CoverUrl,
            "Background" => detail?.BackgroundUrl,
            "Banner" => detail?.BannerUrl,
            _ => null,
        };

    private static string BuildArtworkVariantIdentity(EntityAsset asset)
    {
        var stableSource = !string.IsNullOrWhiteSpace(asset.ImageUrl)
            ? asset.ImageUrl
            : !string.IsNullOrWhiteSpace(asset.LocalImagePath)
                ? asset.LocalImagePath
                : asset.Id.ToString("D");

        return $"{asset.AssetTypeValue}|{stableSource}";
    }

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

    private static string BuildArtworkUploadPath(
        AssetPathService assetPathService,
        string ownerEntityKind,
        Guid ownerEntityId,
        string normalizedAssetType,
        Guid variantId,
        string? contentType) =>
        assetPathService.GetCentralAssetPath(
            ownerEntityKind,
            ownerEntityId,
            normalizedAssetType,
            variantId,
            BuildArtworkExtension(normalizedAssetType, contentType));

    private static string BuildArtworkExtension(string normalizedAssetType, string? contentType) =>
        string.Equals(normalizedAssetType, "Logo", StringComparison.OrdinalIgnoreCase)
        || string.Equals(normalizedAssetType, "DiscArt", StringComparison.OrdinalIgnoreCase)
        || string.Equals(normalizedAssetType, "ClearArt", StringComparison.OrdinalIgnoreCase)
            ? ".png"
            : string.Equals(contentType, "image/png", StringComparison.OrdinalIgnoreCase)
                ? ".png"
                : ".jpg";

    private static string BuildArtworkVariantStreamUrl(Guid variantId) =>
        $"/stream/artwork/{variantId}";

    private static string GetArtworkCanonicalKey(string normalizedAssetType) =>
        normalizedAssetType switch
        {
            "CoverArt" => MetadataFieldConstants.CoverUrl,
            "SquareArt" => "square",
            "Background" => "background",
            "Banner" => "banner",
            "Logo" => "logo",
            "DiscArt" => "disc",
            "ClearArt" => "clearart",
            "SeasonPoster" => "season_poster",
            "SeasonThumb" => "season_thumb",
            "EpisodeStill" => "episode_still",
            _ => throw new ArgumentOutOfRangeException(nameof(normalizedAssetType), normalizedAssetType, "Unsupported artwork type."),
        };

    private static async Task SyncArtworkCanonicalAsync(
        Guid entityId,
        string assetType,
        EntityAsset? preferredAsset,
        ICanonicalValueRepository canonicalRepo,
        IEntityAssetRepository entityAssetRepo,
        CancellationToken ct)
    {
        var canonicalKey = GetArtworkCanonicalKey(assetType);

        if (preferredAsset is null)
        {
            await canonicalRepo.DeleteByKeyAsync(entityId, canonicalKey, ct);

            if (string.Equals(assetType, "CoverArt", StringComparison.OrdinalIgnoreCase))
            {
                await canonicalRepo.UpsertBatchAsync(
                    ArtworkCanonicalHelper.CreateFlags(
                        entityId,
                        coverState: "missing",
                        coverSource: null,
                        heroState: "missing",
                        lastScoredAt: DateTimeOffset.UtcNow,
                        settled: true),
                    ct);
            }

            return;
        }

        var canonicals = ArtworkCanonicalHelper.CreatePreferredAssetCanonicals(
            entityId,
            preferredAsset,
            DateTimeOffset.UtcNow);

        if (string.Equals(assetType, "CoverArt", StringComparison.OrdinalIgnoreCase))
        {
            var coverSource = string.Equals(preferredAsset.SourceProvider, "user_upload", StringComparison.OrdinalIgnoreCase)
                ? "manual"
                : !string.IsNullOrWhiteSpace(preferredAsset.SourceProvider)
                    ? "provider"
                    : "stored";

            canonicals.AddRange(ArtworkCanonicalHelper.CreateFlags(
                entityId,
                coverState: "present",
                coverSource: coverSource,
                heroState: "missing",
                lastScoredAt: DateTimeOffset.UtcNow,
                settled: true));
        }

        await canonicalRepo.UpsertBatchAsync(canonicals, ct);
    }

    private static string? GetArtworkCanonicalValue(IReadOnlyList<CanonicalValue> canonicals, string assetType)
    {
        var canonicalKey = GetArtworkCanonicalKey(assetType);
        return canonicals.FirstOrDefault(c =>
            string.Equals(c.Key, canonicalKey, StringComparison.OrdinalIgnoreCase))?.Value;
    }

    private static string InferSyntheticArtworkOrigin(
        IReadOnlyList<CanonicalValue> canonicals,
        string assetType,
        string? detailArtworkSource)
    {
        if (string.Equals(assetType, "CoverArt", StringComparison.OrdinalIgnoreCase))
        {
            var coverSource = canonicals.FirstOrDefault(c =>
                string.Equals(c.Key, MetadataFieldConstants.CoverSource, StringComparison.OrdinalIgnoreCase))?.Value
                ?? detailArtworkSource;

            return coverSource switch
            {
                "manual" => "Uploaded",
                "provider" => "Provider",
                "embedded" => "Stored",
                _ => "Stored",
            };
        }

        return "Stored";
    }

    private static ArtworkVariantEnvelope MapArtworkVariant(EntityAsset asset) =>
        new(
            asset.Id,
            asset.AssetTypeValue,
            BuildArtworkVariantStreamUrl(asset.Id),
            asset.IsPreferred,
            string.Equals(asset.SourceProvider, "user_upload", StringComparison.OrdinalIgnoreCase)
                ? "Uploaded"
                : !string.IsNullOrWhiteSpace(asset.SourceProvider)
                    ? "Provider"
                    : "Stored",
            FormatArtworkProviderName(asset.SourceProvider),
            CanDelete: string.Equals(asset.SourceProvider, "user_upload", StringComparison.OrdinalIgnoreCase) || asset.IsUserOverride,
            CreatedAt: asset.CreatedAt);

    private static string? FormatArtworkProviderName(string? sourceProvider) =>
        string.IsNullOrWhiteSpace(sourceProvider)
            ? null
            : sourceProvider switch
            {
                "fanart_tv" => "Fanart.tv",
                "user_upload" => "Library Upload",
                _ => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(sourceProvider.Replace('_', ' ')),
            };

    private sealed record ArtworkEditorEnvelope(
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
        [property: JsonPropertyName("scope_summary")] string? ScopeSummary,
        [property: JsonPropertyName("read_only_hint")] string? ReadOnlyHint,
        [property: JsonPropertyName("can_edit_fields")] bool CanEditFields,
        [property: JsonPropertyName("can_edit_artwork")] bool CanEditArtwork);
    private sealed record ArtworkSlotEnvelope(
        [property: JsonPropertyName("asset_type")] string AssetType,
        [property: JsonPropertyName("variants")] IReadOnlyList<ArtworkVariantEnvelope> Variants);
    private sealed record ArtworkVariantEnvelope(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("asset_type")] string AssetType,
        [property: JsonPropertyName("image_url")] string? ImageUrl,
        [property: JsonPropertyName("is_preferred")] bool IsPreferred,
        [property: JsonPropertyName("origin")] string Origin,
        [property: JsonPropertyName("provider_name")] string? ProviderName,
        [property: JsonPropertyName("can_delete")] bool CanDelete,
        [property: JsonPropertyName("created_at")] DateTimeOffset? CreatedAt);
    private sealed record ArtworkResolutionContext(
        Guid RequestedEntityId,
        Guid? WorkId,
        Guid? RootWorkId,
        Guid? PrimaryAssetId,
        Guid? RootPrimaryAssetId,
        IReadOnlyList<Guid> ArtworkEntityIds,
        Guid? PreferredArtworkEntityId);
    private sealed record ArtworkWorkResolutionRow(
        string WorkId,
        string RootWorkId,
        string? PrimaryAssetId,
        string? RootPrimaryAssetId);
    private sealed record ArtworkAssetResolutionRow(
        string AssetId,
        string WorkId,
        string RootWorkId,
        string? RootPrimaryAssetId);
    private sealed record EditorAssetSample(string AssetIdValue, string? FilePath)
    {
        public Guid? AssetId => TryParseGuid(AssetIdValue);
    }
    private sealed record EditorLaunchWorkRow(string WorkId, string MediaType, string WorkKind, string? ParentWorkId, string? RootWorkId);
    private sealed record EditorLaunchAssetRow(string AssetId, string? FilePath, string WorkId, string MediaType, string WorkKind, string? ParentWorkId, string? RootWorkId);
    private sealed record EditorLaunchContext(
        Guid LaunchEntityId,
        string LaunchEntityKind,
        Guid WorkId,
        Guid? ParentWorkId,
        Guid RootWorkId,
        string MediaType,
        string WorkKind,
        Guid? RepresentativeAssetId,
        string? RepresentativeMediaFilePath);
    private sealed record EditorScopeContext(
        Guid LaunchEntityId,
        string LaunchEntityKind,
        string MediaType,
        string EditorMode,
        IReadOnlyList<string> AvailableTabs,
        string? ContentTabLabel,
        bool SupportsFileTab,
        MediaEditorTargetSummaryEnvelope CurrentTargetSummary,
        MediaEditorIdentitySummaryEnvelope IdentitySummary,
        IReadOnlyDictionary<string, bool> FieldLockMap,
        IReadOnlyList<string> DisplayOverrideKeys,
        IReadOnlyDictionary<string, string> DisplayOverrides,
        string InitialScope,
        IReadOnlyList<EditorScopeResolution> Scopes);
    private sealed record EditorScopeResolution(
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
