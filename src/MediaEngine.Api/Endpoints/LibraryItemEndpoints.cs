using Dapper;
using MediaEngine.Api.Models;
using MediaEngine.Api.Security;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
using MediaEngine.Storage;
using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Services;

namespace MediaEngine.Api.Endpoints;

/// <summary>
/// LibraryItem API endpoints — unified view of all ingested media items with
/// confidence scoring, match source, status filtering, and review integration.
/// </summary>
public static class LibraryItemEndpoints
{
    public static IEndpointRouteBuilder MapLibraryItemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/library/items")
                       .WithTags("LibraryItems");

        // ── GET /library/items ───────────────────────────────────────────────
        group.MapGet("", async (
            int? offset,
            int? limit,
            string? search,
            string? type,
            string? status,
            double? minConfidence,
            string? matchSource,
            bool? duplicatesOnly,
            string? sort,
            int? maxDays,
            ILibraryItemRepository repo,
            CancellationToken ct) =>
        {
            var query = new LibraryItemQuery(
                Offset: offset ?? 0,
                Limit: limit ?? 50,
                Search: search,
                MediaType: type,
                Status: status,
                MinConfidence: minConfidence,
                MatchSource: matchSource,
                DuplicatesOnly: duplicatesOnly ?? false,
                Sort: sort,
                MaxDays: maxDays);

            var result = await repo.GetPageAsync(query, ct);
            return Results.Ok(result);
        })
        .WithName("GetLibraryCatalogItems")
        .WithSummary("Paginated list of all ingested items with filtering.")
        .Produces<LibraryItemsPage>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // ── GET /library/items/{entityId}/detail ─────────────────────────────
        group.MapGet("/{entityId}/detail", async (
            Guid entityId,
            ILibraryItemRepository repo,
            CancellationToken ct) =>
        {
            var detail = await repo.GetDetailAsync(entityId, ct);
            return detail is null
                ? Results.NotFound()
                : Results.Ok(detail);
        })
        .WithName("GetLibraryItemDetail")
        .WithSummary("Full detail for a single libraryItem item.")
        .Produces<LibraryItemDetail>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        // ── GET /library/items/counts ──────────────────────────────────────────────
        group.MapGet("/counts", async (
            ILibraryItemRepository repo,
            CancellationToken ct) =>
        {
            var counts = await repo.GetStatusCountsAsync(ct);
            return Results.Ok(counts);
        })
        .WithName("GetLibraryItemStatusCounts")
        .WithSummary("Status counts for tab badges (All, Staging, Review, Auto, Edited, Duplicate).")
        .Produces<LibraryItemStatusCounts>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // ── GET /library/items/state-counts ───────────────────────────────────────
        // Four-state counts (Registered, NeedsReview, NoMatch, Failed) + trigger breakdown.
        group.MapGet("/state-counts", async (
            Guid? batchId,
            ILibraryItemRepository repo,
            CancellationToken ct) =>
        {
            var counts = await repo.GetFourStateCountsAsync(batchId, ct);
            return Results.Ok(counts);
        })
        .WithName("GetLibraryItemLifecycleCounts")
        .WithSummary("Four-state counts (Registered, NeedsReview, NoMatch, Failed) with trigger breakdown.")
        .Produces<LibraryItemLifecycleCounts>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // ── GET /library/items/type-counts ──────────────────────────────────────
        group.MapGet("/type-counts", async (
            ILibraryItemRepository repo,
            CancellationToken ct) =>
        {
            var counts = await repo.GetMediaTypeCountsAsync(ct);
            return Results.Ok(counts);
        })
        .WithName("GetLibraryItemTypeCounts")
        .WithSummary("Per-media-type item counts.")
        .Produces<Dictionary<string, int>>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // ── POST /library/items/{entityId}/apply-match ────────────────────
        group.MapPost("/{entityId}/apply-match", async (
            Guid entityId,
            ApplyMatchRequest request,
            IMetadataClaimRepository claimRepo,
            IHydrationPipelineService pipeline,
            ICollectionRepository collectionRepo,
            IDatabaseConnection db,
            ISystemActivityRepository activityRepo,
            CancellationToken ct) =>
        {

            var target = TryResolveLibraryItemTarget(entityId, db);
            if (target is null)
                return Results.NotFound($"No current media asset or work target found for {entityId}.");

            var assetId = target.AssetId;
            var workId = target.WorkId;
            var workTitle = target.Title;
            var resolvedMediaType = Enum.TryParse<MediaType>(target.MediaType, true, out var mt) ? mt : MediaType.Unknown;

            var now = DateTimeOffset.UtcNow;
            var claims = new List<MetadataClaim>();

            // User provider GUID for user-locked claims
            var userProviderId = WellKnownProviders.UserManual;

            // Build user-locked claims for provided metadata fields
            void AddClaim(string key, string? value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    claims.Add(new MetadataClaim
                    {
                        Id           = Guid.NewGuid(),
                        EntityId     = assetId,
                        ProviderId   = userProviderId,
                        ClaimKey     = key,
                        ClaimValue   = value,
                        Confidence   = 1.0,
                        ClaimedAt    = now,
                        IsUserLocked = true,
                    });
            }

            // Always write provided metadata as user-locked claims
            AddClaim(MetadataFieldConstants.Title,        request.Title);
            AddClaim("release_year", request.Year);
            AddClaim(MetadataFieldConstants.Author,       request.Author);
            AddClaim("director",     request.Director);
            AddClaim(MetadataFieldConstants.Description,  request.Description);
            AddClaim(MetadataFieldConstants.CoverUrl,    request.CoverUrl);

            bool hydrationTriggered = false;
            string wikidataStatus;

            if (!string.IsNullOrWhiteSpace(request.Qid))
            {
                // QID provided: lock the QID and trigger full hydration
                AddClaim(BridgeIdKeys.WikidataQid, request.Qid);
                wikidataStatus = "confirmed";

                if (claims.Count > 0)
                    await claimRepo.InsertBatchAsync(claims, ct);

                // Upsert all user-locked claims into canonical_values immediately.
                // The hydration pipeline may re-score later, but if it fails silently
                // we still need canonical_values populated for the LibraryItem query to
                // return the correct title, author, QID, cover art, etc.
                if (claims.Count > 0)
                {
                    using var cvConn = db.CreateConnection();
                    foreach (var claim in claims)
                    {
                        using var cvCmd = cvConn.CreateCommand();
                        cvCmd.CommandText = """
                            INSERT INTO canonical_values (entity_id, key, value, last_scored_at, is_conflicted, needs_review)
                            VALUES (@entityId, @key, @value, @scoredAt, 0, 0)
                            ON CONFLICT(entity_id, key) DO UPDATE SET
                                value          = excluded.value,
                                last_scored_at = excluded.last_scored_at,
                                is_conflicted  = 0,
                                needs_review   = 0;
                            """;
                        cvCmd.Parameters.Add("@entityId", Microsoft.Data.Sqlite.SqliteType.Blob).Value = GuidSql.ToBlob(assetId);
                        cvCmd.Parameters.AddWithValue("@key",      claim.ClaimKey);
                        cvCmd.Parameters.AddWithValue("@value",    claim.ClaimValue);
                        cvCmd.Parameters.AddWithValue("@scoredAt", now.ToString("o"));
                        cvCmd.ExecuteNonQuery();
                    }
                }

                // Update work's wikidata_status
                await collectionRepo.UpdateWorkWikidataStatusAsync(workId, "confirmed", ct);

                // Build hints from the claims for the pipeline
                var hints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [BridgeIdKeys.WikidataQid] = request.Qid,
                };
                if (!string.IsNullOrWhiteSpace(request.Title))   hints[MetadataFieldConstants.Title]        = request.Title;
                if (!string.IsNullOrWhiteSpace(request.Year))    hints["release_year"] = request.Year;
                if (!string.IsNullOrWhiteSpace(request.Author))  hints[MetadataFieldConstants.Author]       = request.Author;

                // Trigger full hydration (synchronous, Universe pass).
                // SuppressReviewCreation = true — the curator just approved this item;
                // the pipeline must not bounce it back into review (e.g. language mismatch).
                try
                {
                    await pipeline.RunSynchronousAsync(new HarvestRequest
                    {
                        EntityId               = assetId,
                        EntityType             = EntityType.MediaAsset,
                        MediaType              = resolvedMediaType,
                        Hints                  = hints,
                        Pass                   = HydrationPass.Universe,
                        SuppressReviewCreation = true,
                    }, ct);
                    hydrationTriggered = true;
                }
                catch (Exception)
                {
                    // Hydration failure doesn't fail the match — claims are already written
                    hydrationTriggered = false;
                }

                // Set curator_state = 'registered' — the curator confirmed a QID match (internal DB token)
                using (var conn = db.CreateConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = """
                        UPDATE works SET curator_state = 'registered', rejected_at = NULL
                        WHERE id = @workId
                        """;
                    cmd.Parameters.Add("@workId", Microsoft.Data.Sqlite.SqliteType.Blob).Value = GuidSql.ToBlob(workId);
                    cmd.ExecuteNonQuery();
                }

                // Use the request title if provided, fall back to DB title
                var displayTitle = request.Title ?? workTitle ?? "unknown";

                // Log curator manual approval to the activity ledger
                await activityRepo.LogAsync(new SystemActivityEntry
                {
                    OccurredAt = DateTimeOffset.UtcNow,
                    ActionType = SystemActionType.ReviewItemResolved,
                    CollectionName    = displayTitle,
                    EntityId   = workId,
                    EntityType = "Work",
                    Detail     = $"Registered '{displayTitle}' — QID {request.Qid} confirmed.",
                }, ct);
            }
            else
            {
                // No QID: write metadata claims only, mark as retail-confirmed.
                // Set curator_state so the item stays visible in the libraryItem
                // (the visibility filter requires QID OR review OR curator_state).
                wikidataStatus = "missing";

                using (var conn = db.CreateConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = """
                        UPDATE works SET curator_state = 'registered', rejected_at = NULL
                        WHERE id = @workId
                        """;
                    cmd.Parameters.Add("@workId", Microsoft.Data.Sqlite.SqliteType.Blob).Value = GuidSql.ToBlob(workId);
                    cmd.ExecuteNonQuery();
                }

                if (claims.Count > 0)
                    await claimRepo.InsertBatchAsync(claims, ct);

                await collectionRepo.UpdateWorkWikidataStatusAsync(workId, "missing", ct);

                // Upsert canonical values directly for retail metadata
                if (claims.Count > 0)
                {
                    using var conn = db.CreateConnection();
                    foreach (var claim in claims)
                    {
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = """
                            INSERT INTO canonical_values (entity_id, key, value, last_scored_at, is_conflicted, needs_review)
                            VALUES (@entityId, @key, @value, @scoredAt, 0, 0)
                            ON CONFLICT(entity_id, key) DO UPDATE SET
                                value          = excluded.value,
                                last_scored_at = excluded.last_scored_at,
                                is_conflicted  = 0,
                                needs_review   = 0;
                            """;
                        cmd.Parameters.Add("@entityId", Microsoft.Data.Sqlite.SqliteType.Blob).Value = GuidSql.ToBlob(assetId);
                        cmd.Parameters.AddWithValue("@key",      claim.ClaimKey);
                        cmd.Parameters.AddWithValue("@value",    claim.ClaimValue);
                        cmd.Parameters.AddWithValue("@scoredAt", now.ToString("o"));
                        cmd.ExecuteNonQuery();
                    }
                }

                // Trigger Stage 2 (Wikidata bridge resolution) — retail is already done.
                // The bridge worker uses existing bridge IDs only for automatic resolution.
                try
                {
                    var pipelineResult = await pipeline.RunSynchronousAsync(new HarvestRequest
                    {
                        EntityId               = assetId,
                        EntityType             = EntityType.MediaAsset,
                        MediaType              = resolvedMediaType,
                        SkipRetailStage        = true,
                        SuppressReviewCreation = true,
                    }, ct);
                    hydrationTriggered = true;

                    // If Stage 2 resolved a QID, update the status
                    if (!string.IsNullOrWhiteSpace(pipelineResult.WikidataQid))
                    {
                        wikidataStatus = "confirmed";
                        await collectionRepo.UpdateWorkWikidataStatusAsync(workId, "confirmed", ct);
                    }
                }
                catch (Exception)
                {
                    // Pipeline failure doesn't fail the match — claims are already written
                    hydrationTriggered = false;
                }

                // Log retail-only approval to the activity ledger
                var retailTitle = request.Title ?? workTitle ?? "unknown";
                await activityRepo.LogAsync(new SystemActivityEntry
                {
                    OccurredAt = DateTimeOffset.UtcNow,
                    ActionType = SystemActionType.ReviewItemResolved,
                    CollectionName    = retailTitle,
                    EntityId   = workId,
                    EntityType = "Work",
                    Detail     = $"Match applied for '{retailTitle}' — retail metadata only (no Wikidata QID).",
                }, ct);
            }

            // Dismiss any pending review items for this asset — runs for both QID and
            // no-QID paths because the user explicitly approved the item in both cases.
            using (var conn = db.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    UPDATE review_queue
                    SET status = 'Resolved', resolved_at = @now, resolved_by = 'user:curator'
                    WHERE entity_id IN (@assetId, @workId) AND status = 'Pending'
                    """;
                cmd.Parameters.Add("@assetId", Microsoft.Data.Sqlite.SqliteType.Blob).Value = GuidSql.ToBlob(assetId);
                cmd.Parameters.Add("@workId", Microsoft.Data.Sqlite.SqliteType.Blob).Value = GuidSql.ToBlob(workId);
                cmd.Parameters.AddWithValue("@now",     now.ToString("o"));
                cmd.ExecuteNonQuery();
            }

            return Results.Ok(new ApplyMatchResponse
            {
                EntityId           = entityId,
                WikidataStatus     = wikidataStatus,
                ClaimsWritten      = claims.Count,
                HydrationTriggered = hydrationTriggered,
                Message            = wikidataStatus == "confirmed"
                    ? $"Registered '{request.Title ?? workTitle ?? "item"}' with QID {request.Qid}."
                    : "Match applied. Metadata claims written.",
            });
        })
        .WithName("ApplyLibraryItemMatch")
        .WithSummary("Apply a selected match to a libraryItem item. Provide a QID to register the item.")
        .Produces<ApplyMatchResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        // ── POST /library/items/{entityId}/create-manual ─────────────────
        group.MapPost("/{entityId}/create-manual", async (
            Guid entityId,
            CreateManualRequest request,
            IMetadataClaimRepository claimRepo,
            ICollectionRepository collectionRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                return Results.BadRequest("Title is required for manual entry.");

            var target = TryResolveLibraryItemTarget(entityId, db);
            if (target is null)
                return Results.NotFound($"No current media asset or work target found for {entityId}.");

            var assetId = target.AssetId;
            var workId = target.WorkId;

            var now          = DateTimeOffset.UtcNow;
            var userProvider = WellKnownProviders.UserManual;
            var claims       = new List<MetadataClaim>();

            void AddClaim(string key, string? value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    claims.Add(new MetadataClaim
                    {
                        Id           = Guid.NewGuid(),
                        EntityId     = assetId,
                        ProviderId   = userProvider,
                        ClaimKey     = key,
                        ClaimValue   = value,
                        Confidence   = 1.0,
                        ClaimedAt    = now,
                        IsUserLocked = true,
                    });
            }

            AddClaim(MetadataFieldConstants.Title,        request.Title);
            AddClaim("release_year", request.Year);
            AddClaim(MetadataFieldConstants.Author,       request.Author);
            AddClaim(MetadataFieldConstants.Description,  request.Description);

            if (claims.Count > 0)
            {
                await claimRepo.InsertBatchAsync(claims, ct);

                // Upsert canonical values so changes appear immediately
                using var conn = db.CreateConnection();
                foreach (var claim in claims)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = """
                        INSERT INTO canonical_values (entity_id, key, value, last_scored_at, is_conflicted, needs_review)
                        VALUES (@entityId, @key, @value, @scoredAt, 0, 0)
                        ON CONFLICT(entity_id, key) DO UPDATE SET
                            value          = excluded.value,
                            last_scored_at = excluded.last_scored_at,
                            is_conflicted  = 0,
                            needs_review   = 0;
                        """;
                    cmd.Parameters.Add("@entityId", Microsoft.Data.Sqlite.SqliteType.Blob).Value = GuidSql.ToBlob(assetId);
                    cmd.Parameters.AddWithValue("@key",      claim.ClaimKey);
                    cmd.Parameters.AddWithValue("@value",    claim.ClaimValue);
                    cmd.Parameters.AddWithValue("@scoredAt", now.ToString("o"));
                    cmd.ExecuteNonQuery();
                }
            }

            await collectionRepo.UpdateWorkWikidataStatusAsync(workId, "manual", ct);

            return Results.Ok(new CreateManualResponse
            {
                EntityId       = entityId,
                WikidataStatus = "manual",
                ClaimsWritten  = claims.Count,
                Message        = $"Manual entry created with {claims.Count} user-locked fields. Item marked for future Universe sweep.",
            });
        })
        .WithName("CreateManualLibraryItemEntry")
        .WithSummary("Create a manual metadata entry for a libraryItem item with no provider match.")
        .Produces<CreateManualResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        // ── DELETE /library/items/{entityId} ───────────────────────────────
        group.MapDelete("/{entityId}", async (
            Guid entityId,
            IDatabaseConnection db,
            WorkHierarchyMaintenanceService hierarchyMaintenance,
            ISystemActivityRepository activityRepo,
            CancellationToken ct) =>
        {
            // 1. Resolve all media asset file paths + QID for this work
            var filePaths = new List<string>();
            Guid? collectionId = null;
            string? workTitle = null;
            Guid? parentWorkId = null;

            using (var conn = db.CreateConnection())
            {
                // Get file paths
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = """
                        SELECT ma.file_path_root
                        FROM editions e
                        INNER JOIN media_assets ma ON ma.edition_id = e.id
                        WHERE e.work_id = @workId
                        """;
                    cmd.Parameters.Add("@workId", Microsoft.Data.Sqlite.SqliteType.Blob).Value = GuidSql.ToBlob(entityId);
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var path = reader.GetString(0);
                        if (!string.IsNullOrWhiteSpace(path))
                            filePaths.Add(path);
                    }
                }

                // Get collection ID for cleanup
                using (var cmd2 = conn.CreateCommand())
                {
                    cmd2.CommandText = "SELECT collection_id, parent_work_id FROM works WHERE id = @workId";
                    cmd2.Parameters.Add("@workId", Microsoft.Data.Sqlite.SqliteType.Blob).Value = GuidSql.ToBlob(entityId);
                    using var reader2 = cmd2.ExecuteReader();
                    if (reader2.Read())
                    {
                        collectionId = reader2.IsDBNull(0) ? null : GuidSql.FromDb(reader2.GetValue(0));
                        parentWorkId = reader2.IsDBNull(1) ? null : GuidSql.FromDb(reader2.GetValue(1));
                    }
                }

                using (var cmd3 = conn.CreateCommand())
                {
                    cmd3.CommandText = @"
                        SELECT cv.value FROM canonical_values cv
                        INNER JOIN media_assets ma ON ma.id = cv.entity_id
                        INNER JOIN editions e ON e.id = ma.edition_id
                        WHERE e.work_id = @workId AND cv.key = 'title'
                        LIMIT 1";
                    cmd3.Parameters.Add("@workId", Microsoft.Data.Sqlite.SqliteType.Blob).Value = GuidSql.ToBlob(entityId);
                    workTitle = cmd3.ExecuteScalar()?.ToString();
                }
            }

            if (filePaths.Count == 0)
                return Results.NotFound($"No media assets found for work {entityId}.");

            // 2. Delete physical files from disk + best-effort legacy cover/hero cleanup
            foreach (var filePath in filePaths)
            {
                try
                {
                    if (File.Exists(filePath))
                        File.Delete(filePath);

                    var dir = Path.GetDirectoryName(filePath);
                    if (dir is not null)
                    {
                        // Remove empty directory
                        if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                        {
                            try { Directory.Delete(dir); } catch { /* best-effort */ }
                        }
                    }
                }
                catch (IOException)
                {
                    // File may be locked or already deleted — continue with DB cleanup
                }
            }

            // 3. Delete review_queue entries for assets of this work
            using (var conn = db.CreateConnection())
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    DELETE FROM review_queue
                    WHERE entity_id IN (
                        SELECT ma.id FROM editions e
                        INNER JOIN media_assets ma ON ma.edition_id = e.id
                        WHERE e.work_id = @workId
                    )
                    """;
                cmd.Parameters.Add("@workId", Microsoft.Data.Sqlite.SqliteType.Blob).Value = GuidSql.ToBlob(entityId);
                cmd.ExecuteNonQuery();
            }

            // 4. Delete the work (CASCADE handles editions → media_assets → claims → canonical_values)
            using (var conn = db.CreateConnection())
            {
                CleanupEntityAssetFiles(conn, entityId);
                conn.Execute("DELETE FROM entity_assets WHERE entity_id = @workId;", new { workId = entityId });
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM works WHERE id = @workId";
                cmd.Parameters.Add("@workId", Microsoft.Data.Sqlite.SqliteType.Blob).Value = GuidSql.ToBlob(entityId);
                cmd.ExecuteNonQuery();
            }

            await hierarchyMaintenance.CleanupEmptyParentsAsync(parentWorkId, ct);

            // 6. Clean up collection if no remaining works
            if (collectionId is not null)
            {
                using var conn = db.CreateConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    DELETE FROM collections WHERE id = @collectionId
                    AND NOT EXISTS (SELECT 1 FROM works WHERE collection_id = @collectionId)
                    """;
                cmd.Parameters.Add("@collectionId", Microsoft.Data.Sqlite.SqliteType.Blob).Value = GuidSql.ToBlob(collectionId.Value);
                cmd.ExecuteNonQuery();
            }

            // 7. Log activity
            await activityRepo.LogAsync(new SystemActivityEntry
            {
                OccurredAt  = DateTimeOffset.UtcNow,
                ActionType  = SystemActionType.MediaRemoved,
                CollectionName     = workTitle,
                EntityId    = entityId,
                EntityType  = "Work",
                Detail      = $"Removed '{workTitle ?? "unknown"}' — {filePaths.Count} file(s) deleted from disk and database.",
            }, ct);

            return Results.Ok(new { EntityId = entityId, FilesDeleted = filePaths.Count, Message = $"Removed '{workTitle ?? "unknown"}' and {filePaths.Count} file(s)." });
        })
        .WithName("DeleteLibraryCatalogItem")
        .WithSummary("Permanently remove a work and all its files from the library.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        // ── POST /library/items/{entityId}/reject ────────────────────────────
        group.MapPost("/{entityId}/reject", async (
            Guid entityId,
            ISystemActivityRepository activityRepo,
            IReviewQueueRepository reviewRepo,
            IDatabaseConnection db,
            IConfigurationLoader configLoader,
            IEventPublisher publisher,
            CancellationToken ct) =>
        {
            var target = TryResolveLibraryItemTarget(entityId, db);
            if (target is null || target.FilePath is null)
                return Results.NotFound($"No current media asset or work target found for {entityId}.");

            var assetId = target.AssetId;
            var workId = target.WorkId;
            var currentFilePath = target.FilePath;
            var workTitle = target.Title;

            // Resolve the library root to build the .data/staging/rejected/ path.
            var core = configLoader.LoadCore();
            var libraryRoot = core.LibraryRoot;
            if (string.IsNullOrWhiteSpace(libraryRoot))
                return Results.BadRequest("LibraryRoot is not configured. Cannot determine rejected folder.");

            var rejectedDir = Path.Combine(libraryRoot, ".data", "staging", "rejected");
            Directory.CreateDirectory(rejectedDir);

            var fileName    = Path.GetFileName(currentFilePath);
            var newFilePath = Path.Combine(rejectedDir, fileName);

            // Avoid collisions: append asset ID suffix when a same-named file already exists.
            if (File.Exists(newFilePath) && !string.Equals(currentFilePath, newFilePath, StringComparison.OrdinalIgnoreCase))
                newFilePath = Path.Combine(rejectedDir, $"{Path.GetFileNameWithoutExtension(fileName)}__{assetId}{Path.GetExtension(fileName)}");

            // Move the file if it is not already in the rejected folder.
            if (!string.Equals(currentFilePath, newFilePath, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (File.Exists(currentFilePath))
                        File.Move(currentFilePath, newFilePath, overwrite: false);
                }
                catch (IOException ex)
                {
                    return Results.Problem($"Could not move file to rejected folder: {ex.Message}");
                }
            }

            // Update the file_path_root in the database.
            using (var conn = db.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE media_assets SET file_path_root = @path WHERE id = @id";
                cmd.Parameters.AddWithValue("@path", newFilePath);
                cmd.Parameters.Add("@id", Microsoft.Data.Sqlite.SqliteType.Blob).Value = GuidSql.ToBlob(assetId);
                cmd.ExecuteNonQuery();
            }

            // Dismiss any pending review items for this asset.
            using (var conn = db.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    UPDATE review_queue
                    SET status = 'Dismissed', resolved_at = @now, resolved_by = 'user:reject'
                    WHERE entity_id IN (@assetId, @workId) AND status = 'Pending'
                    """;
                cmd.Parameters.Add("@assetId", Microsoft.Data.Sqlite.SqliteType.Blob).Value = GuidSql.ToBlob(assetId);
                cmd.Parameters.Add("@workId", Microsoft.Data.Sqlite.SqliteType.Blob).Value = GuidSql.ToBlob(workId);
                cmd.Parameters.AddWithValue("@now",     DateTimeOffset.UtcNow.ToString("o"));
                cmd.ExecuteNonQuery();
            }

            // Set curator_state = 'rejected' on the works table.
            using (var conn = db.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    UPDATE works SET curator_state = 'rejected', rejected_at = @now
                    WHERE id = @workId
                    """;
                cmd.Parameters.Add("@workId", Microsoft.Data.Sqlite.SqliteType.Blob).Value = GuidSql.ToBlob(workId);
                cmd.Parameters.AddWithValue("@now",    DateTimeOffset.UtcNow.ToString("o"));
                cmd.ExecuteNonQuery();
            }

            // Log to the activity ledger.
            await activityRepo.LogAsync(new SystemActivityEntry
            {
                OccurredAt = DateTimeOffset.UtcNow,
                ActionType = SystemActionType.FileRejected,
                CollectionName    = workTitle,
                EntityId   = workId,
                EntityType = "Work",
                Detail     = $"Rejected '{workTitle ?? "unknown"}' — file moved to .staging/rejected/.",
            }, ct);

            await publisher.PublishAsync(SignalREvents.ReviewItemResolved, new
            {
                entity_id = workId,
                action = "rejected",
            }, ct);

            return Results.Ok(new { EntityId = entityId, NewFilePath = newFilePath, Message = $"Rejected '{workTitle ?? "unknown"}'." });
        })
        .WithName("RejectLibraryCatalogItem")
        .WithSummary("Reject a libraryItem item: move its file to .staging/rejected/ and mark it as Rejected.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        // ── POST /library/items/batch/approve ──────────────────────────────────────
        group.MapPost("/batch/approve", async (
            BatchLibraryItemRequest request,
            IMetadataClaimRepository claimRepo,
            ICollectionRepository collectionRepo,
            IReviewQueueRepository reviewRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            if (request.EntityIds is null || request.EntityIds.Length == 0)
                return Results.BadRequest("No entity IDs provided.");

            int processed = 0;

            foreach (var entityId in request.EntityIds)
            {
                try
                {
                    // Mark as missing universe (retail-only match)
                    await collectionRepo.UpdateWorkWikidataStatusAsync(entityId, "missing", ct);

                    // Dismiss any pending review items for this work's assets
                    using var conn = db.CreateConnection();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = """
                        UPDATE review_queue SET status = 'Resolved', resolved_at = @now
                        WHERE status = 'Pending' AND entity_id IN (
                            SELECT ma.id FROM editions e
                            INNER JOIN media_assets ma ON ma.edition_id = e.id
                            WHERE e.work_id = @workId
                        )
                        """;
                    cmd.Parameters.Add("@workId", Microsoft.Data.Sqlite.SqliteType.Blob).Value = GuidSql.ToBlob(entityId);
                    cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("o"));
                    cmd.ExecuteNonQuery();

                    processed++;
                }
                catch { /* Skip failed items */ }
            }

            return Results.Ok(new BatchLibraryItemResponse
            {
                ProcessedCount  = processed,
                TotalRequested  = request.EntityIds.Length,
                Message         = $"Approved {processed} of {request.EntityIds.Length} items.",
            });
        })
        .WithName("BatchApproveLibraryCatalogItems")
        .WithSummary("Approve multiple libraryItem items in batch (marks as missing universe, dismisses reviews).")
        .Produces<BatchLibraryItemResponse>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // ── POST /library/items/batch/delete ───────────────────────────────────────
        group.MapPost("/batch/delete", async (
            BatchLibraryItemRequest request,
            IDatabaseConnection db,
            WorkHierarchyMaintenanceService hierarchyMaintenance,
            ISystemActivityRepository activityRepo,
            CancellationToken ct) =>
        {
            if (request.EntityIds is null || request.EntityIds.Length == 0)
                return Results.BadRequest("No entity IDs provided.");

            int processed   = 0;
            int filesRemoved = 0;

            foreach (var entityId in request.EntityIds)
            {
                try
                {
                    var filePaths = new List<string>();
                    Guid? collectionId = null;
                    string? workTitle = null;
                    Guid? parentWorkId = null;

                    using (var conn = db.CreateConnection())
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = """
                                SELECT ma.file_path_root
                                FROM editions e
                                INNER JOIN media_assets ma ON ma.edition_id = e.id
                                WHERE e.work_id = @workId
                                """;
                            cmd.Parameters.Add("@workId", Microsoft.Data.Sqlite.SqliteType.Blob).Value = GuidSql.ToBlob(entityId);
                            using var reader = cmd.ExecuteReader();
                            while (reader.Read())
                            {
                                var path = reader.GetString(0);
                                if (!string.IsNullOrWhiteSpace(path))
                                    filePaths.Add(path);
                            }
                        }

                        using (var cmd2 = conn.CreateCommand())
                        {
                            cmd2.CommandText = "SELECT collection_id, parent_work_id FROM works WHERE id = @workId";
                            cmd2.Parameters.Add("@workId", Microsoft.Data.Sqlite.SqliteType.Blob).Value = GuidSql.ToBlob(entityId);
                            using var reader2 = cmd2.ExecuteReader();
                            if (reader2.Read())
                            {
                                collectionId = reader2.IsDBNull(0) ? null : GuidSql.FromDb(reader2.GetValue(0));
                                parentWorkId = reader2.IsDBNull(1) ? null : GuidSql.FromDb(reader2.GetValue(1));
                            }
                        }

                        using (var cmd3 = conn.CreateCommand())
                        {
                            cmd3.CommandText = @"
                                SELECT cv.value FROM canonical_values cv
                                INNER JOIN media_assets ma ON ma.id = cv.entity_id
                                INNER JOIN editions e ON e.id = ma.edition_id
                                WHERE e.work_id = @workId AND cv.key = 'title'
                                LIMIT 1";
                            cmd3.Parameters.Add("@workId", Microsoft.Data.Sqlite.SqliteType.Blob).Value = GuidSql.ToBlob(entityId);
                            workTitle = cmd3.ExecuteScalar()?.ToString();
                        }
                    }

                    if (filePaths.Count == 0) continue;

                    foreach (var filePath in filePaths)
                    {
                        try
                        {
                            if (File.Exists(filePath)) File.Delete(filePath);
                            var dir = Path.GetDirectoryName(filePath);
                            if (dir is not null)
                            {
                                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                                {
                                    try { Directory.Delete(dir); } catch { /* best-effort */ }
                                }
                            }
                            filesRemoved++;
                        }
                        catch (IOException) { }
                    }

                    using (var conn = db.CreateConnection())
                    {
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = """
                            DELETE FROM review_queue
                            WHERE entity_id IN (
                                SELECT ma.id FROM editions e
                                INNER JOIN media_assets ma ON ma.edition_id = e.id
                                WHERE e.work_id = @workId
                            )
                            """;
                        cmd.Parameters.Add("@workId", Microsoft.Data.Sqlite.SqliteType.Blob).Value = GuidSql.ToBlob(entityId);
                        cmd.ExecuteNonQuery();
                    }

                    using (var conn = db.CreateConnection())
                    {
                        CleanupEntityAssetFiles(conn, entityId);
                        conn.Execute("DELETE FROM entity_assets WHERE entity_id = @workId;", new { workId = entityId });
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = "DELETE FROM works WHERE id = @workId";
                        cmd.Parameters.Add("@workId", Microsoft.Data.Sqlite.SqliteType.Blob).Value = GuidSql.ToBlob(entityId);
                        cmd.ExecuteNonQuery();
                    }

                    await hierarchyMaintenance.CleanupEmptyParentsAsync(parentWorkId, ct);

                    if (collectionId is not null)
                    {
                        using var conn = db.CreateConnection();
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = """
                            DELETE FROM collections WHERE id = @collectionId
                            AND NOT EXISTS (SELECT 1 FROM works WHERE collection_id = @collectionId)
                            """;
                        cmd.Parameters.Add("@collectionId", Microsoft.Data.Sqlite.SqliteType.Blob).Value = GuidSql.ToBlob(collectionId.Value);
                        cmd.ExecuteNonQuery();
                    }

                    await activityRepo.LogAsync(new SystemActivityEntry
                    {
                        OccurredAt = DateTimeOffset.UtcNow,
                        ActionType = SystemActionType.MediaRemoved,
                        CollectionName    = workTitle,
                        EntityId   = entityId,
                        EntityType = "Work",
                        Detail     = $"Batch removed '{workTitle ?? "unknown"}' — {filePaths.Count} file(s) deleted.",
                    }, ct);

                    processed++;
                }
                catch { /* Skip failed items */ }
            }

            return Results.Ok(new BatchLibraryItemResponse
            {
                ProcessedCount  = processed,
                TotalRequested  = request.EntityIds.Length,
                Message         = $"Deleted {processed} of {request.EntityIds.Length} items ({filesRemoved} files removed).",
            });
        })
        .WithName("BatchDeleteLibraryCatalogItems")
        .WithSummary("Permanently delete multiple libraryItem items and their files in batch.")
        .Produces<BatchLibraryItemResponse>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // ── POST /library/items/batch/reject ───────────────────────────────────────
        group.MapPost("/batch/reject", async (
            BatchLibraryItemRequest request,
            IDatabaseConnection db,
            ISystemActivityRepository activityRepo,
            IConfigurationLoader configLoader,
            CancellationToken ct) =>
        {
            if (request.EntityIds is null || request.EntityIds.Length == 0)
                return Results.BadRequest("No entity IDs provided.");

            var core = configLoader.LoadCore();
            var libraryRoot = core.LibraryRoot;
            if (string.IsNullOrWhiteSpace(libraryRoot))
                return Results.BadRequest("LibraryRoot is not configured. Cannot determine rejected folder.");

            var rejectedDir = Path.Combine(libraryRoot, ".data", "staging", "rejected");
            Directory.CreateDirectory(rejectedDir);

            int processed = 0;

            foreach (var entityId in request.EntityIds)
            {
                try
                {
                    Guid? assetId = null;
                    string? currentFilePath = null;
                    string? workTitle       = null;

                    using (var conn = db.CreateConnection())
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = """
                                SELECT ma.id, ma.file_path_root
                                FROM editions e
                                INNER JOIN media_assets ma ON ma.edition_id = e.id
                                WHERE e.work_id = @workId
                                LIMIT 1
                                """;
                            cmd.Parameters.Add("@workId", Microsoft.Data.Sqlite.SqliteType.Blob).Value = GuidSql.ToBlob(entityId);
                            using var reader = cmd.ExecuteReader();
                            if (reader.Read())
                            {
                                assetId         = GuidSql.FromDb(reader.GetValue(0));
                                currentFilePath = reader.IsDBNull(1) ? null : reader.GetString(1);
                            }
                        }

                        using (var cmd2 = conn.CreateCommand())
                        {
                            cmd2.CommandText = @"
                                SELECT cv.value FROM canonical_values cv
                                INNER JOIN media_assets ma ON ma.id = cv.entity_id
                                INNER JOIN editions e ON e.id = ma.edition_id
                                WHERE e.work_id = @workId AND cv.key = 'title'
                                LIMIT 1";
                            cmd2.Parameters.Add("@workId", Microsoft.Data.Sqlite.SqliteType.Blob).Value = GuidSql.ToBlob(entityId);
                            workTitle = cmd2.ExecuteScalar()?.ToString();
                        }
                    }

                    if (!assetId.HasValue || currentFilePath is null) continue;

                    var fileName    = Path.GetFileName(currentFilePath);
                    var newFilePath = Path.Combine(rejectedDir, fileName);

                    if (File.Exists(newFilePath) && !string.Equals(currentFilePath, newFilePath, StringComparison.OrdinalIgnoreCase))
                        newFilePath = Path.Combine(rejectedDir, $"{Path.GetFileNameWithoutExtension(fileName)}__{assetId.Value}{Path.GetExtension(fileName)}");

                    if (!string.Equals(currentFilePath, newFilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            if (File.Exists(currentFilePath))
                                File.Move(currentFilePath, newFilePath, overwrite: false);
                        }
                        catch (IOException) { /* Skip if file is locked or already moved */ }
                    }

                    using (var conn = db.CreateConnection())
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "UPDATE media_assets SET file_path_root = @path WHERE id = @id";
                        cmd.Parameters.AddWithValue("@path", newFilePath);
                        cmd.Parameters.Add("@id", Microsoft.Data.Sqlite.SqliteType.Blob).Value = GuidSql.ToBlob(assetId.Value);
                        cmd.ExecuteNonQuery();
                    }

                    using (var conn = db.CreateConnection())
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = """
                            UPDATE review_queue
                            SET status = 'Dismissed', resolved_at = @now, resolved_by = 'user:reject'
                            WHERE entity_id = @assetId AND status = 'Pending'
                            """;
                        cmd.Parameters.Add("@assetId", Microsoft.Data.Sqlite.SqliteType.Blob).Value = GuidSql.ToBlob(assetId.Value);
                        cmd.Parameters.AddWithValue("@now",     DateTimeOffset.UtcNow.ToString("o"));
                        cmd.ExecuteNonQuery();
                    }

                    using (var conn = db.CreateConnection())
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = """
                            UPDATE works SET curator_state = 'rejected', rejected_at = @now
                            WHERE id = @workId
                            """;
                        cmd.Parameters.Add("@workId", Microsoft.Data.Sqlite.SqliteType.Blob).Value = GuidSql.ToBlob(entityId);
                        cmd.Parameters.AddWithValue("@now",    DateTimeOffset.UtcNow.ToString("o"));
                        cmd.ExecuteNonQuery();
                    }

                    await activityRepo.LogAsync(new SystemActivityEntry
                    {
                        OccurredAt = DateTimeOffset.UtcNow,
                        ActionType = SystemActionType.FileRejected,
                        CollectionName    = workTitle,
                        EntityId   = entityId,
                        EntityType = "Work",
                        Detail     = $"Batch rejected '{workTitle ?? "unknown"}' — file moved to .staging/rejected/.",
                    }, ct);

                    processed++;
                }
                catch { /* Skip failed items */ }
            }

            return Results.Ok(new BatchLibraryItemResponse
            {
                ProcessedCount = processed,
                TotalRequested = request.EntityIds.Length,
                Message        = $"Rejected {processed} of {request.EntityIds.Length} items.",
            });
        })
        .WithName("BatchRejectLibraryCatalogItems")
        .WithSummary("Reject multiple libraryItem items in batch: move files to .staging/rejected/ and mark as Rejected.")
        .Produces<BatchLibraryItemResponse>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // ── POST /library/items/{entityId}/recover ───────────────────────────
        group.MapPost("/{entityId:guid}/recover", async (
            Guid entityId,
            IDatabaseConnection db,
            IReviewQueueRepository reviewRepo,
            ISystemActivityRepository activityRepo,
            IEventPublisher publisher,
            CancellationToken ct) =>
        {
            // Clear the rejected state on the work.
            using var conn = db.CreateConnection();
            var affected = conn.Execute("""
                UPDATE works SET curator_state = NULL, rejected_at = NULL
                WHERE id = @workId AND curator_state = 'rejected'
                """, new { workId = entityId });

            if (affected == 0)
                return Results.NotFound($"Work {entityId} is not in rejected state.");

            // Create a new review queue entry so the item goes back to InReview.
            var assetId = conn.QueryFirstOrDefault<Guid?>("""
                SELECT ma.id FROM editions e
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                WHERE e.work_id = @workId LIMIT 1
                """, new { workId = entityId });

            var reviewId = Guid.Empty;
            if (assetId.HasValue)
            {
                reviewId = Guid.NewGuid();
                using var cmd2 = conn.CreateCommand();
                cmd2.CommandText = """
                    INSERT INTO review_queue
                        (id, entity_id, entity_type, trigger, status, confidence_score, detail, created_at)
                    VALUES
                        (@id, @entityId, 'MediaAsset', @trigger, 'Pending', 0, @detail, @createdAt)
                    """;
                cmd2.Parameters.Add("@id", Microsoft.Data.Sqlite.SqliteType.Blob).Value = GuidSql.ToBlob(reviewId);
                cmd2.Parameters.Add("@entityId", Microsoft.Data.Sqlite.SqliteType.Blob).Value = GuidSql.ToBlob(assetId.Value);
                cmd2.Parameters.AddWithValue("@trigger",   "UserFixMatch");
                cmd2.Parameters.AddWithValue("@detail",    "Un-rejected by user — returned to review queue.");
                cmd2.Parameters.AddWithValue("@createdAt", DateTimeOffset.UtcNow.ToString("o"));
                cmd2.ExecuteNonQuery();
            }

            try
            {
                await activityRepo.LogAsync(new SystemActivityEntry
                {
                    ActionType = SystemActionType.ItemUnrejected,
                    EntityId   = entityId,
                    EntityType = "Work",
                    Detail     = "Un-rejected — returned to review queue for re-evaluation.",
                }, ct);
            }
            catch (Exception) { /* history is supplementary */ }

            await publisher.PublishAsync(SignalREvents.ReviewItemCreated, new
            {
                review_item_id = reviewId,
                entity_id = entityId,
                trigger = "UserFixMatch",
            }, ct);

            return Results.Ok(new { message = "Item un-rejected and returned to review queue." });
        })
        .WithName("RecoverLibraryCatalogItem")
        .WithSummary("Recover a previously rejected libraryItem item — removes the Rejected status.")
        .Produces(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // ── POST /library/items/{entityId}/provisional ─────────────────────────
        group.MapPost("/{entityId:guid}/provisional", async (
            Guid entityId,
            ProvisionalMetadataRequest body,
            IDatabaseConnection db,
            IReviewQueueRepository reviewRepo,
            ISystemActivityRepository activityRepo,
            IEventPublisher publisher,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Title))
                return Results.BadRequest("Title is required for provisional metadata.");

            // Serialize the full provisional metadata as JSON for storage.
            var metadataJson = System.Text.Json.JsonSerializer.Serialize(body);

            // Set curator_state = 'provisional' on the works table.
            using var conn = db.CreateConnection();
            var affected = conn.Execute("""
                UPDATE works
                SET curator_state = 'provisional',
                    provisional_metadata_json = @json
                WHERE id = @workId
                """, new { workId = entityId, json = metadataJson });

            if (affected == 0)
                return Results.NotFound($"Work {entityId} not found.");

            // Dismiss any pending review items — the curator has taken ownership.
            var assetId = conn.QueryFirstOrDefault<Guid?>("""
                SELECT ma.id FROM editions e
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                WHERE e.work_id = @workId LIMIT 1
                """, new { workId = entityId });

            if (assetId.HasValue)
            {
                conn.Execute("""
                    UPDATE review_queue
                    SET status = 'Dismissed', resolved_at = @now, resolved_by = 'user:provisional'
                    WHERE entity_id = @assetId AND status = 'Pending'
                    """, new { assetId = assetId.Value, now = DateTimeOffset.UtcNow.ToString("o") });
            }

            // Store provisional metadata as user-locked claims at confidence 1.0.
            if (assetId.HasValue)
            {
                var now = DateTimeOffset.UtcNow.ToString("o");
                var localProviderId = WellKnownProviders.LocalProcessor;

                var fields = new Dictionary<string, string?>
                {
                    [MetadataFieldConstants.Title]       = body.Title,
                    [MetadataFieldConstants.Author]      = body.Creator,
                    [MetadataFieldConstants.Year]        = body.Year,
                    [MetadataFieldConstants.Description] = body.Description,
                    ["narrator"]    = body.Narrator,
                    [BridgeIdKeys.Isbn]        = body.Isbn,
                    ["director"]    = body.Director,
                    [MetadataFieldConstants.Runtime]     = body.Runtime,
                    ["host"]        = body.Host,
                    ["writer"]      = body.Writer,
                    [MetadataFieldConstants.Artist]      = body.Artist,
                };

                foreach (var (key, value) in fields)
                {
                    if (string.IsNullOrWhiteSpace(value)) continue;

                    conn.Execute("""
                        INSERT OR REPLACE INTO metadata_claims
                            (id, entity_id, claim_key, claim_value, provider_id, confidence, is_user_locked, claimed_at)
                        VALUES
                            (@id, @entityId, @key, @value, @providerId, 1.0, 1, @now)
                        """, new
                        {
                            id = Guid.NewGuid(),
                            entityId = assetId.Value,
                            key,
                            value,
                            providerId = localProviderId,
                            now,
                        });

                    // Also upsert canonical values so the UI sees them immediately.
                    conn.Execute("""
                        INSERT INTO canonical_values (entity_id, key, value, winning_provider_id, is_conflicted, needs_review, last_scored_at)
                        VALUES (@entityId, @key, @value, @providerId, 0, 0, @now)
                        ON CONFLICT(entity_id, key) DO UPDATE SET
                            value = excluded.value,
                            winning_provider_id = excluded.winning_provider_id,
                            is_conflicted = 0,
                            needs_review = 0,
                            last_scored_at = excluded.last_scored_at
                        """, new
                        {
                            entityId = assetId.Value,
                            key,
                            value,
                            providerId = localProviderId,
                            now,
                        });
                }
            }

            try
            {
                await activityRepo.LogAsync(new SystemActivityEntry
                {
                    ActionType = SystemActionType.ItemProvisional,
                    EntityId   = entityId,
                    EntityType = "Work",
                    CollectionName    = body.Title,
                    Detail     = $"Marked '{body.Title}' as provisional with curator-entered metadata.",
                }, ct);
            }
            catch (Exception) { /* history is supplementary */ }

            await publisher.PublishAsync(SignalREvents.ReviewItemResolved, new
            {
                entity_id = entityId,
                action = "provisional",
            }, ct);

            return Results.Ok(new
            {
                EntityId = entityId,
                State    = "Provisional",
                Title    = body.Title,
                Message  = $"'{body.Title}' marked as provisional.",
            });
        })
        .WithName("MarkProvisional")
        .WithSummary("Mark a libraryItem item as provisional with curator-entered metadata.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        // ── GET /library/items/{entityId}/history ────────────────────────────
        // entityId is a work ID from the libraryItem listing. system_activity entries
        // may reference the work ID or the asset ID, so we query for both.
        group.MapGet("/{entityId:guid}/history", async (
            Guid entityId,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            // Resolve asset ID from work ID so we can find events logged against either.
            Guid? assetId;
            using (var conn = db.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT ma.id
                    FROM editions e
                    INNER JOIN media_assets ma ON ma.edition_id = e.id
                    WHERE e.work_id = @workId
                    LIMIT 1
                    """;
                cmd.Parameters.Add("@workId", Microsoft.Data.Sqlite.SqliteType.Blob).Value = GuidSql.ToBlob(entityId);
                var result = cmd.ExecuteScalar();
                assetId = result is null or DBNull ? null : GuidSql.FromDb(result);
            }

            // Query system_activity for both work ID and asset ID.
            var resolvedAssetId = assetId ?? entityId;

            using var conn2 = db.CreateConnection();
            using var cmd2 = conn2.CreateCommand();
            cmd2.CommandText = """
                SELECT id, occurred_at, action_type, entity_id, detail
                FROM system_activity
                WHERE entity_id = @workId OR entity_id = @assetId
                ORDER BY occurred_at DESC
                LIMIT 200
                """;
            cmd2.Parameters.Add("@workId", Microsoft.Data.Sqlite.SqliteType.Blob).Value = GuidSql.ToBlob(entityId);
            cmd2.Parameters.Add("@assetId", Microsoft.Data.Sqlite.SqliteType.Blob).Value = GuidSql.ToBlob(resolvedAssetId);

            var entries = new List<object>();
            using var reader = cmd2.ExecuteReader();
            while (reader.Read())
            {
                entries.Add(new
                {
                    id = reader.GetInt64(0).ToString(),
                    entity_id = reader.IsDBNull(3) ? entityId : GuidSql.FromDb(reader.GetValue(3)),
                    occurred_at = reader.IsDBNull(1) ? DateTimeOffset.UtcNow : DateTimeOffset.Parse(reader.GetString(1)),
                    event_type = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    label = reader.IsDBNull(2) ? "" : FormatActionTypeLabel(reader.GetString(2)),
                    detail = reader.IsDBNull(4) ? (string?)null : reader.GetString(4),
                });
            }

            return Results.Ok(entries);
        })
        .WithName("GetLibraryCatalogItemHistory")
        .WithSummary("Get processing history timeline for a libraryItem item")
        .Produces(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        return app;
    }

    private static LibraryItemTarget? TryResolveLibraryItemTarget(Guid entityId, IDatabaseConnection db)
    {
        using var conn = db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                ma.id,
                e.work_id,
                ma.file_path_root,
                (
                    SELECT cv.value
                    FROM canonical_values cv
                    WHERE cv.entity_id IN (ma.id, e.work_id)
                      AND cv.key IN ('title', 'show_name', 'episode_title')
                      AND cv.value IS NOT NULL
                      AND cv.value <> ''
                    ORDER BY CASE cv.key WHEN 'title' THEN 0 WHEN 'show_name' THEN 1 ELSE 2 END
                    LIMIT 1
                ) AS title,
                (
                    SELECT cv.value
                    FROM canonical_values cv
                    WHERE cv.entity_id = ma.id
                      AND cv.key = 'media_type'
                      AND cv.value IS NOT NULL
                      AND cv.value <> ''
                    LIMIT 1
                ) AS media_type
            FROM media_assets ma
            INNER JOIN editions e ON e.id = ma.edition_id
            WHERE ma.id = @entityId
               OR e.work_id = @entityId
            LIMIT 1;
            """;
        cmd.Parameters.Add("@entityId", Microsoft.Data.Sqlite.SqliteType.Blob).Value = GuidSql.ToBlob(entityId);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;

        return new LibraryItemTarget(
            GuidSql.FromDb(reader.GetValue(0)),
            GuidSql.FromDb(reader.GetValue(1)),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    private static void CleanupEntityAssetFiles(System.Data.IDbConnection conn, Guid entityId)
    {
        var rows = conn.Query<(string? LocalImagePath, string? LocalImagePathSmall, string? LocalImagePathMedium, string? LocalImagePathLarge)>(
            """
            SELECT local_image_path   AS LocalImagePath,
                   local_image_path_s AS LocalImagePathSmall,
                   local_image_path_m AS LocalImagePathMedium,
                   local_image_path_l AS LocalImagePathLarge
            FROM entity_assets
            WHERE entity_id = @entityId
            """,
            new { entityId });

        foreach (var path in rows
                     .SelectMany(row => new[] { row.LocalImagePath, row.LocalImagePathSmall, row.LocalImagePathMedium, row.LocalImagePathLarge })
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            TryDeleteManagedAssetFile(path!);
        }
    }

    private static void TryDeleteManagedAssetFile(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!IsManagedAssetPath(fullPath))
                return;

            if (File.Exists(fullPath))
                File.Delete(fullPath);

            PruneEmptyManagedAssetParents(fullPath);
        }
        catch
        {
            // Asset file cleanup is best-effort; the database delete must still complete.
        }
    }

    private static bool IsManagedAssetPath(string fullPath)
    {
        var normalized = fullPath.Replace('\\', '/');
        return normalized.Contains("/.data/assets/", StringComparison.OrdinalIgnoreCase);
    }

    private static void PruneEmptyManagedAssetParents(string fullPath)
    {
        var current = Path.GetDirectoryName(fullPath);
        while (!string.IsNullOrWhiteSpace(current)
               && IsManagedAssetPath(current)
               && !string.Equals(Path.GetFileName(current), "assets", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (!Directory.Exists(current) || Directory.EnumerateFileSystemEntries(current).Any())
                    return;

                Directory.Delete(current);
                current = Path.GetDirectoryName(current);
            }
            catch
            {
                return;
            }
        }
    }
    /// <summary>Converts a system_activity action_type into a human-readable label for the History tab.</summary>
    private static string FormatActionTypeLabel(string actionType) => actionType switch
    {
        "FileDetected"             => "File detected",
        "FileIngested"             => "File ingested",
        "MetadataExtracted"        => "Metadata extracted",
        "ConfidenceScored"         => "Confidence scored",
        "MovedToStaging"           => "Moved to staging",
        "Promoted"                 => "Promoted to library",
        "ReviewItemCreated"        => "Sent for review",
        "ReviewItemResolved"       => "Review resolved",
        "HydrationStarted"         => "Enrichment started",
        "HydrationCompleted"       => "Enrichment complete",
        "WikidataMatched"          => "Identified on Wikidata",
        "WikidataMatchFailed"      => "No Wikidata match found",
        "RetailEnriched"           => "Cover art retrieved",
        "RetailEnrichFailed"       => "No cover art found",
        "MetadataManualOverride"   => "Manual metadata override",
        "MetadataWrittenToFile"    => "Metadata written to file",
        "CoverArtSaved"            => "Cover art saved",
        "HeroBannerGenerated"      => "Hero banner generated",
        "CollectionCreated"               => "Collection created",
        "CollectionAssigned"              => "Assigned to collection",
        "PersonHydrated"           => "Person enriched",
        "FileRejected"             => "Rejected",
        "Recovered"                => "Recovered from rejection",
        "FileHashed"               => "Content fingerprinted",
        "DuplicateSkipped"         => "Duplicate skipped",
        "EntityChainCreated"       => "Library records created",
        "HydrationEnqueued"        => "Queued for enrichment",
        _                          => actionType.Replace("_", " "),
    };

    private sealed record LibraryItemTarget(
        Guid AssetId,
        Guid WorkId,
        string? FilePath,
        string? Title,
        string? MediaType);
}
