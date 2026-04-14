using Dapper;
using MediaEngine.Api.Models;
using MediaEngine.Api.Security;
using MediaEngine.Domain;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Endpoints;

/// <summary>
/// Library endpoints for operational health, batch edits, and universe curation.
/// </summary>
public static class LibraryEndpoints
{
    public static IEndpointRouteBuilder MapLibraryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/library")
                       .WithTags("Library");

        // â”€â”€ GET /library/overview â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        group.MapGet("/overview", async (
            IRegistryRepository registryRepo,
            IReviewQueueRepository reviewRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            // 1. Four-state counts (Identified, InReview, Provisional, Rejected) + trigger breakdown
            var fourState = await registryRepo.GetFourStateCountsAsync(ct: ct);

            // 2. Shared projection summary
            var projection = await registryRepo.GetProjectionSummaryAsync(ct);

            // 3. Media type counts
            var mediaTypeCounts = await registryRepo.GetMediaTypeCountsAsync(ct);

            // 4. Review pending count
            var reviewTotal = await reviewRepo.GetPendingCountAsync(ct);

            // 5. Remaining aggregates via direct SQL for fields not exposed
            //    by existing repository interfaces.
            using var conn = db.CreateConnection();

            // Recently added counts (24h, 7d, 30d) â€” based on media_asset created_at
            var now = DateTimeOffset.UtcNow;
            var epoch24h = now.AddHours(-24).ToUnixTimeSeconds();
            var epoch7d  = now.AddDays(-7).ToUnixTimeSeconds();
            var epoch30d = now.AddDays(-30).ToUnixTimeSeconds();

            var added24h = await conn.ExecuteScalarAsync<int>("""
                SELECT COUNT(DISTINCT w.id)
                FROM works w
                INNER JOIN editions e     ON e.work_id = w.id
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                WHERE ma.created_at >= @since
                """, new { since = epoch24h });

            var added7d = await conn.ExecuteScalarAsync<int>("""
                SELECT COUNT(DISTINCT w.id)
                FROM works w
                INNER JOIN editions e     ON e.work_id = w.id
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                WHERE ma.created_at >= @since
                """, new { since = epoch7d });

            var added30d = await conn.ExecuteScalarAsync<int>("""
                SELECT COUNT(DISTINCT w.id)
                FROM works w
                INNER JOIN editions e     ON e.work_id = w.id
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                WHERE ma.created_at >= @since
                """, new { since = epoch30d });

            // Pipeline state counts from identity_jobs
            var pipelineRows = await conn.QueryAsync<(string State, int Count)>("""
                SELECT state AS State, COUNT(*) AS Count
                FROM identity_jobs
                GROUP BY state
                """);
            var pipelineStates = pipelineRows.ToDictionary(r => r.State, r => r.Count);

            // Pipeline success rate: Completed / (Completed + Failed)
            pipelineStates.TryGetValue("Completed", out var completedCount);
            pipelineStates.TryGetValue("Failed", out var failedCount);
            var pipelineTotal = completedCount + failedCount;
            var successRate = pipelineTotal > 0 ? (double)completedCount / pipelineTotal : 1.0;

            var dto = new LibraryOverviewDto
            {
                TotalItems          = projection.TotalItems,
                Added24h            = added24h,
                Added7d             = added7d,
                Added30d            = added30d,
                PipelineStates      = pipelineStates,
                PipelineSuccessRate = Math.Round(successRate, 4),
                ReviewCategories    = fourState.TriggerCounts.ToDictionary(kv => kv.Key, kv => kv.Value),
                ReviewTotal         = reviewTotal,
                WithQid             = projection.WithQid,
                WithoutQid          = projection.WithoutQid,
                EnrichedStage3      = projection.EnrichedStage3,
                NotEnrichedStage3   = projection.NotEnrichedStage3,
                UniverseAssigned    = projection.UniverseAssigned,
                UniverseUnassigned  = projection.UniverseUnassigned,
                StaleItems          = projection.StaleItems,
                MediaTypeCounts     = mediaTypeCounts,
                HiddenByQualityGate = projection.HiddenByQualityGate,
                ArtPending          = projection.ArtPending,
                RetailNeedsReview   = projection.RetailNeedsReview,
                QidNoMatch          = projection.QidNoMatch,
                CompletedWithArt    = projection.CompletedWithArt,
            };

            return Results.Ok(dto);
        })
        .WithName("GetLibraryOverview")
        .WithSummary("Aggregated operational health summary for the library dashboard.")
        .Produces<LibraryOverviewDto>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // â”€â”€ POST /library/batch-edit/preview â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        group.MapPost("/batch-edit/preview", async (
            LibraryBatchEditRequest request,
            ICanonicalValueRepository canonicalRepo,
            CancellationToken ct) =>
        {
            if (request.EntityIds.Count == 0 || request.FieldChanges.Count == 0)
                return Results.BadRequest("Must provide entity IDs and field changes.");

            // Batch-fetch all canonical values for the requested entities in one query
            var allCanonicals = await canonicalRepo.GetByEntitiesAsync(request.EntityIds, ct);

            var changes = new List<LibraryFieldChangePreview>();
            foreach (var change in request.FieldChanges)
            {
                var oldValueCounts = new Dictionary<string, int>();
                foreach (var entityId in request.EntityIds)
                {
                    var entityCanonicals = allCanonicals.TryGetValue(entityId, out var vals)
                        ? vals
                        : [];
                    var current = entityCanonicals.FirstOrDefault(c => c.Key == change.Key);
                    var oldVal = current?.Value ?? "(empty)";
                    oldValueCounts[oldVal] = oldValueCounts.GetValueOrDefault(oldVal, 0) + 1;
                }
                changes.Add(new LibraryFieldChangePreview
                {
                    Key = change.Key,
                    NewValue = change.Value,
                    OldValueCounts = oldValueCounts,
                });
            }

            return Results.Ok(new LibraryBatchEditPreview
            {
                AffectedCount = request.EntityIds.Count,
                Changes = changes,
            });
        })
        .WithName("PreviewBatchEdit")
        .WithSummary("Dry-run preview of a batch edit operation.")
        .Produces<LibraryBatchEditPreview>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // â”€â”€ POST /library/batch-edit â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        group.MapPost("/batch-edit", async (
            LibraryBatchEditRequest request,
            ICanonicalValueRepository canonicalRepo,
            IMetadataClaimRepository claimRepo,
            CancellationToken ct) =>
        {
            if (request.EntityIds.Count == 0 || request.FieldChanges.Count == 0)
                return Results.BadRequest("Must provide entity IDs and field changes.");

            var updatedCount = 0;
            var failedIds = new List<Guid>();
            var errors = new List<string>();

            foreach (var entityId in request.EntityIds)
            {
                try
                {
                    var claims = new List<MetadataClaim>();
                    var canonicals = new List<CanonicalValue>();

                    foreach (var change in request.FieldChanges)
                    {
                        // Create a user-locked claim that overrides all other sources
                        claims.Add(new MetadataClaim
                        {
                            Id = Guid.NewGuid(),
                            EntityId = entityId,
                            ClaimKey = change.Key,
                            ClaimValue = change.Value,
                            ProviderId = WellKnownProviders.UserManual,
                            Confidence = 1.0,
                            IsUserLocked = true,
                            ClaimedAt = DateTimeOffset.UtcNow,
                        });

                        // Update the canonical value directly
                        canonicals.Add(new CanonicalValue
                        {
                            EntityId = entityId,
                            Key = change.Key,
                            Value = change.Value,
                            LastScoredAt = DateTimeOffset.UtcNow,
                            IsConflicted = false,
                            WinningProviderId = WellKnownProviders.UserManual,
                            NeedsReview = false,
                        });
                    }

                    await claimRepo.InsertBatchAsync(claims, ct);
                    await canonicalRepo.UpsertBatchAsync(canonicals, ct);
                    updatedCount++;
                }
                catch (Exception ex)
                {
                    failedIds.Add(entityId);
                    errors.Add($"{entityId}: {ex.Message}");
                }
            }

            return Results.Ok(new LibraryBatchEditResult
            {
                UpdatedCount = updatedCount,
                FailedIds = failedIds,
                Errors = errors,
            });
        })
        .WithName("ApplyBatchEdit")
        .WithSummary("Apply batch field edits to multiple items.")
        .Produces<LibraryBatchEditResult>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // â”€â”€ GET /library/universe-candidates â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        group.MapGet("/universe-candidates", async (IDatabaseConnection db, CancellationToken ct) =>
        {
            using var conn = db.CreateConnection();

            // Find works that have series_qid/franchise_qid/fictional_universe_qid in canonical_values
            // but are NOT assigned to a collection (works.collection_id IS NULL)
            var candidates = await conn.QueryAsync<UniverseCandidateDto>("""
                SELECT DISTINCT
                    w.id AS WorkId,
                    ma.id AS EntityId,
                    COALESCE(cv_title.value, w.title, 'Unknown') AS Title,
                    COALESCE(cv_mt.value, '') AS MediaType,
                    COALESCE(cv_sq.value, cv_fq.value, cv_uq.value) AS CandidateQid,
                    CASE
                        WHEN cv_sq.value IS NOT NULL THEN 'series'
                        WHEN cv_fq.value IS NOT NULL THEN 'franchise'
                        ELSE 'universe'
                    END AS CandidateType,
                    COALESCE(cv_sq.value, cv_fq.value, cv_uq.value) AS CandidateLabel
                FROM works w
                INNER JOIN editions e ON e.work_id = w.id
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                LEFT JOIN canonical_values cv_title ON cv_title.entity_id = ma.id AND cv_title.key = 'title'
                LEFT JOIN canonical_values cv_mt ON cv_mt.entity_id = ma.id AND cv_mt.key = 'media_type'
                LEFT JOIN canonical_values cv_sq ON cv_sq.entity_id = ma.id AND cv_sq.key = 'series_qid'
                    AND cv_sq.value IS NOT NULL AND cv_sq.value != ''
                LEFT JOIN canonical_values cv_fq ON cv_fq.entity_id = ma.id AND cv_fq.key = 'franchise_qid'
                    AND cv_fq.value IS NOT NULL AND cv_fq.value != ''
                LEFT JOIN canonical_values cv_uq ON cv_uq.entity_id = ma.id AND cv_uq.key = 'fictional_universe_qid'
                    AND cv_uq.value IS NOT NULL AND cv_uq.value != ''
                LEFT JOIN canonical_values cv_review ON cv_review.entity_id = ma.id AND cv_review.key = 'universe_review_status'
                WHERE w.collection_id IS NULL
                  AND (cv_sq.value IS NOT NULL OR cv_fq.value IS NOT NULL OR cv_uq.value IS NOT NULL)
                  AND (cv_review.value IS NULL OR cv_review.value != 'rejected')
                ORDER BY cv_title.value
                LIMIT 200
                """);

            return Results.Ok(candidates.ToList());
        })
        .WithName("GetUniverseCandidates")
        .WithSummary("Items with universe-related QIDs but no collection assignment.")
        .Produces<List<UniverseCandidateDto>>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // â”€â”€ POST /library/universe-candidates/{workId}/accept â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        group.MapPost("/universe-candidates/{workId:guid}/accept", async (
            Guid workId,
            UniverseAcceptRequest request,
            ICollectionRepository collectionRepo,
            CancellationToken ct) =>
        {
            // Find or create the collection for the target QID
            var collection = await collectionRepo.FindByQidAsync(request.TargetCollectionQid, ct);
            if (collection is null)
            {
                // Create a new ContentGroup collection for this QID
                collection = new Collection
                {
                    Id = Guid.NewGuid(),
                    WikidataQid = request.TargetCollectionQid,
                    DisplayName = request.TargetCollectionQid, // Will be enriched later
                    CollectionType = "ContentGroup",
                    Resolution = "materialized",
                    Scope = "library",
                    IsEnabled = true,
                };
                await collectionRepo.UpsertAsync(collection, ct);
            }

            await collectionRepo.AssignWorkToCollectionAsync(workId, collection.Id, ct);
            return Results.Ok(new { assigned = true, collection_id = collection.Id });
        })
        .WithName("AcceptUniverseCandidate")
        .WithSummary("Accept a universe assignment for a work.")
        .RequireAdminOrCurator();

        // â”€â”€ POST /library/universe-candidates/{workId}/reject â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        group.MapPost("/universe-candidates/{workId:guid}/reject", async (
            Guid workId,
            ICanonicalValueRepository canonicalRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            // Find the entity ID (media asset) for this work
            using var conn = db.CreateConnection();
            var entityId = await conn.QueryFirstOrDefaultAsync<Guid?>("""
                SELECT ma.id FROM media_assets ma
                INNER JOIN editions e ON e.id = ma.edition_id
                WHERE e.work_id = @workId
                LIMIT 1
                """, new { workId });

            if (entityId is null)
                return Results.NotFound();

            // Mark as reviewed/rejected so it doesn't reappear in the pending queue
            await canonicalRepo.UpsertBatchAsync([new CanonicalValue
            {
                EntityId = entityId.Value,
                Key = "universe_review_status",
                Value = "rejected",
                LastScoredAt = DateTimeOffset.UtcNow,
                IsConflicted = false,
                WinningProviderId = WellKnownProviders.UserManual,
                NeedsReview = false,
            }], ct);

            return Results.Ok(new { rejected = true });
        })
        .WithName("RejectUniverseCandidate")
        .WithSummary("Reject a universe candidate for a work.")
        .RequireAdminOrCurator();

        // â”€â”€ POST /library/universe-candidates/batch-accept â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        group.MapPost("/universe-candidates/batch-accept", async (
            UniverseBatchAcceptRequest request,
            ICollectionRepository collectionRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            using var conn = db.CreateConnection();
            var accepted = 0;

            foreach (var workId in request.WorkIds)
            {
                try
                {
                    // Get the best candidate QID for this work
                    var candidateQid = await conn.QueryFirstOrDefaultAsync<string?>("""
                        SELECT COALESCE(cv_sq.value, cv_fq.value, cv_uq.value)
                        FROM works w
                        INNER JOIN editions e ON e.work_id = w.id
                        INNER JOIN media_assets ma ON ma.edition_id = e.id
                        LEFT JOIN canonical_values cv_sq ON cv_sq.entity_id = ma.id AND cv_sq.key = 'series_qid'
                            AND cv_sq.value IS NOT NULL AND cv_sq.value != ''
                        LEFT JOIN canonical_values cv_fq ON cv_fq.entity_id = ma.id AND cv_fq.key = 'franchise_qid'
                            AND cv_fq.value IS NOT NULL AND cv_fq.value != ''
                        LEFT JOIN canonical_values cv_uq ON cv_uq.entity_id = ma.id AND cv_uq.key = 'fictional_universe_qid'
                            AND cv_uq.value IS NOT NULL AND cv_uq.value != ''
                        WHERE w.id = @workId
                        LIMIT 1
                        """, new { workId });

                    if (string.IsNullOrEmpty(candidateQid)) continue;

                    // Strip URI prefix and label suffix if present
                    if (candidateQid.Contains('/')) candidateQid = candidateQid.Split('/').Last();
                    if (candidateQid.Contains("::")) candidateQid = candidateQid.Split("::")[0];

                    var collection = await collectionRepo.FindByQidAsync(candidateQid, ct);
                    if (collection is null)
                    {
                        collection = new Collection
                        {
                            Id = Guid.NewGuid(),
                            WikidataQid = candidateQid,
                            DisplayName = candidateQid,
                            CollectionType = "ContentGroup",
                            Resolution = "materialized",
                            Scope = "library",
                            IsEnabled = true,
                        };
                        await collectionRepo.UpsertAsync(collection, ct);
                    }

                    await collectionRepo.AssignWorkToCollectionAsync(workId, collection.Id, ct);
                    accepted++;
                }
                catch { /* skip failed items */ }
            }

            return Results.Ok(new { accepted_count = accepted });
        })
        .WithName("BatchAcceptUniverseCandidates")
        .WithSummary("Batch accept universe assignments.")
        .RequireAdminOrCurator();

        // â”€â”€ GET /library/universe-unlinked â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        group.MapGet("/universe-unlinked", async (IDatabaseConnection db, CancellationToken ct) =>
        {
            using var conn = db.CreateConnection();

            var unlinked = await conn.QueryAsync<UnlinkedWorkDto>("""
                SELECT DISTINCT
                    w.id AS WorkId,
                    ma.id AS EntityId,
                    COALESCE(cv_title.value, w.title, 'Unknown') AS Title,
                    COALESCE(cv_mt.value, '') AS MediaType,
                    cv_qid.value AS WikidataQid
                FROM works w
                INNER JOIN editions e ON e.work_id = w.id
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                INNER JOIN canonical_values cv_qid ON cv_qid.entity_id = ma.id
                    AND cv_qid.key = 'wikidata_qid'
                    AND cv_qid.value IS NOT NULL AND cv_qid.value != ''
                    AND cv_qid.value NOT LIKE 'NF%'
                LEFT JOIN canonical_values cv_title ON cv_title.entity_id = ma.id AND cv_title.key = 'title'
                LEFT JOIN canonical_values cv_mt ON cv_mt.entity_id = ma.id AND cv_mt.key = 'media_type'
                LEFT JOIN canonical_values cv_sq ON cv_sq.entity_id = ma.id AND cv_sq.key = 'series_qid'
                    AND cv_sq.value IS NOT NULL AND cv_sq.value != ''
                LEFT JOIN canonical_values cv_fq ON cv_fq.entity_id = ma.id AND cv_fq.key = 'franchise_qid'
                    AND cv_fq.value IS NOT NULL AND cv_fq.value != ''
                LEFT JOIN canonical_values cv_uq ON cv_uq.entity_id = ma.id AND cv_uq.key = 'fictional_universe_qid'
                    AND cv_uq.value IS NOT NULL AND cv_uq.value != ''
                LEFT JOIN canonical_values cv_review ON cv_review.entity_id = ma.id AND cv_review.key = 'universe_review_status'
                WHERE w.collection_id IS NULL
                  AND cv_sq.value IS NULL AND cv_fq.value IS NULL AND cv_uq.value IS NULL
                  AND (cv_review.value IS NULL OR cv_review.value != 'rejected')
                ORDER BY cv_title.value
                LIMIT 200
                """);

            return Results.Ok(unlinked.ToList());
        })
        .WithName("GetUniverseUnlinked")
        .WithSummary("Works with Wikidata QID but no universe-related properties.")
        .Produces<List<UnlinkedWorkDto>>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // â”€â”€ POST /library/universe-assign â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        group.MapPost("/universe-assign", async (
            UniverseManualAssignRequest request,
            ICollectionRepository collectionRepo,
            CancellationToken ct) =>
        {
            await collectionRepo.AssignWorkToCollectionAsync(request.WorkId, request.CollectionId, ct);
            return Results.Ok(new { assigned = true });
        })
        .WithName("ManualUniverseAssign")
        .WithSummary("Manually assign a work to an existing collection.")
        .RequireAdminOrCurator();

        return app;
    }
}


