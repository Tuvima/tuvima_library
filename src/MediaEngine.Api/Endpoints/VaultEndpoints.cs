using Dapper;
using MediaEngine.Api.Models;
using MediaEngine.Api.Security;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Endpoints;

/// <summary>
/// Vault overview API endpoints — aggregated operational health summary for
/// the Vault Overview dashboard.
/// </summary>
public static class VaultEndpoints
{
    public static IEndpointRouteBuilder MapVaultEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/vault")
                       .WithTags("Vault");

        // ── GET /vault/overview ─────────────────────────────────────────────
        group.MapGet("/overview", async (
            IRegistryRepository registryRepo,
            IReviewQueueRepository reviewRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            // 1. Four-state counts (Identified, InReview, Provisional, Rejected) + trigger breakdown
            var fourState = await registryRepo.GetFourStateCountsAsync(ct: ct);

            // 2. Media type counts
            var mediaTypeCounts = await registryRepo.GetMediaTypeCountsAsync(ct);

            // 3. Review pending count
            var reviewTotal = await reviewRepo.GetPendingCountAsync(ct);

            // 4. Remaining aggregates via direct SQL for fields not exposed
            //    by existing repository interfaces.
            using var conn = db.CreateConnection();

            // Total items = distinct owned Works (with at least one media asset)
            var totalItems = await conn.ExecuteScalarAsync<int>("""
                SELECT COUNT(DISTINCT w.id)
                FROM works w
                INNER JOIN editions e     ON e.work_id = w.id
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                """);

            // Recently added counts (24h, 7d, 30d) — based on media_asset created_at
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

            // QID coverage: works with valid QID vs without
            var withQid = fourState.Identified;
            var withoutQid = totalItems - withQid;
            if (withoutQid < 0) withoutQid = 0;

            // Universe assignment: works with hub_id vs without
            var universeAssigned = await conn.ExecuteScalarAsync<int>("""
                SELECT COUNT(*)
                FROM works w
                INNER JOIN editions e     ON e.work_id = w.id
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                WHERE w.hub_id IS NOT NULL
                """);
            var universeUnassigned = totalItems - universeAssigned;
            if (universeUnassigned < 0) universeUnassigned = 0;

            // Stale items: works whose latest enrichment is older than 30 days
            var staleItems = await conn.ExecuteScalarAsync<int>("""
                SELECT COUNT(DISTINCT w.id)
                FROM works w
                INNER JOIN editions e     ON e.work_id = w.id
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                INNER JOIN canonical_values cv ON cv.entity_id = ma.id
                WHERE cv.key = 'wikidata_qid'
                  AND cv.value IS NOT NULL AND cv.value != ''
                  AND cv.value NOT LIKE 'NF%'
                  AND cv.last_scored_at < @staleThreshold
                """, new { staleThreshold = epoch30d });

            // Stage 3 enrichment — works that have universe enrichment data
            // (presence of franchise_qid or fictional_universe_qid canonical value)
            var enrichedStage3 = await conn.ExecuteScalarAsync<int>("""
                SELECT COUNT(DISTINCT e2.work_id)
                FROM editions e2
                INNER JOIN media_assets ma2 ON ma2.edition_id = e2.id
                INNER JOIN canonical_values cv2 ON cv2.entity_id = ma2.id
                WHERE cv2.key IN ('franchise_qid', 'fictional_universe_qid')
                  AND cv2.value IS NOT NULL AND cv2.value != ''
                """);
            var notEnrichedStage3 = totalItems - enrichedStage3;
            if (notEnrichedStage3 < 0) notEnrichedStage3 = 0;

            var dto = new VaultOverviewDto
            {
                TotalItems          = totalItems,
                Added24h            = added24h,
                Added7d             = added7d,
                Added30d            = added30d,
                PipelineStates      = pipelineStates,
                PipelineSuccessRate = Math.Round(successRate, 4),
                ReviewCategories    = fourState.TriggerCounts.ToDictionary(kv => kv.Key, kv => kv.Value),
                ReviewTotal         = reviewTotal,
                WithQid             = withQid,
                WithoutQid          = withoutQid,
                EnrichedStage3      = enrichedStage3,
                NotEnrichedStage3   = notEnrichedStage3,
                UniverseAssigned    = universeAssigned,
                UniverseUnassigned  = universeUnassigned,
                StaleItems          = staleItems,
                MediaTypeCounts     = mediaTypeCounts,
            };

            return Results.Ok(dto);
        })
        .WithName("GetVaultOverview")
        .WithSummary("Aggregated operational health summary for the Vault Overview dashboard.")
        .Produces<VaultOverviewDto>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // ── POST /vault/batch-edit/preview ────────────────────────────────
        group.MapPost("/batch-edit/preview", async (
            VaultBatchEditRequest request,
            ICanonicalValueRepository canonicalRepo,
            CancellationToken ct) =>
        {
            if (request.EntityIds.Count == 0 || request.FieldChanges.Count == 0)
                return Results.BadRequest("Must provide entity IDs and field changes.");

            // Batch-fetch all canonical values for the requested entities in one query
            var allCanonicals = await canonicalRepo.GetByEntitiesAsync(request.EntityIds, ct);

            var changes = new List<VaultFieldChangePreview>();
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
                changes.Add(new VaultFieldChangePreview
                {
                    Key = change.Key,
                    NewValue = change.Value,
                    OldValueCounts = oldValueCounts,
                });
            }

            return Results.Ok(new VaultBatchEditPreview
            {
                AffectedCount = request.EntityIds.Count,
                Changes = changes,
            });
        })
        .WithName("PreviewBatchEdit")
        .WithSummary("Dry-run preview of a batch edit operation.")
        .Produces<VaultBatchEditPreview>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // ── POST /vault/batch-edit ────────────────────────────────────────
        group.MapPost("/batch-edit", async (
            VaultBatchEditRequest request,
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

            return Results.Ok(new VaultBatchEditResult
            {
                UpdatedCount = updatedCount,
                FailedIds = failedIds,
                Errors = errors,
            });
        })
        .WithName("ApplyBatchEdit")
        .WithSummary("Apply batch field edits to multiple items.")
        .Produces<VaultBatchEditResult>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        return app;
    }
}
