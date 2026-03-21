using MediaEngine.Api.Models;
using MediaEngine.Api.Security;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Endpoints;

/// <summary>
/// Registry API endpoints — unified view of all ingested media items with
/// confidence scoring, match source, status filtering, and review integration.
/// </summary>
public static class RegistryEndpoints
{
    public static IEndpointRouteBuilder MapRegistryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/registry")
                       .WithTags("Registry");

        // ── GET /registry/items ───────────────────────────────────────────────
        group.MapGet("/items", async (
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
            IRegistryRepository repo,
            CancellationToken ct) =>
        {
            var query = new RegistryQuery(
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
        .WithName("GetRegistryItems")
        .WithSummary("Paginated list of all ingested items with filtering.")
        .Produces<RegistryPageResult>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // ── GET /registry/items/{entityId}/detail ─────────────────────────────
        group.MapGet("/items/{entityId}/detail", async (
            Guid entityId,
            IRegistryRepository repo,
            CancellationToken ct) =>
        {
            var detail = await repo.GetDetailAsync(entityId, ct);
            return detail is null
                ? Results.NotFound()
                : Results.Ok(detail);
        })
        .WithName("GetRegistryItemDetail")
        .WithSummary("Full detail for a single registry item.")
        .Produces<RegistryItemDetail>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        // ── GET /registry/counts ──────────────────────────────────────────────
        group.MapGet("/counts", async (
            IRegistryRepository repo,
            CancellationToken ct) =>
        {
            var counts = await repo.GetStatusCountsAsync(ct);
            return Results.Ok(counts);
        })
        .WithName("GetRegistryStatusCounts")
        .WithSummary("Status counts for tab badges (All, Staging, Review, Auto, Edited, Duplicate).")
        .Produces<RegistryStatusCounts>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // ── GET /registry/state-counts ───────────────────────────────────────
        // Four-state counts (Registered, NeedsReview, NoMatch, Failed) + trigger breakdown.
        group.MapGet("/state-counts", async (
            Guid? batchId,
            IRegistryRepository repo,
            CancellationToken ct) =>
        {
            var counts = await repo.GetFourStateCountsAsync(batchId, ct);
            return Results.Ok(counts);
        })
        .WithName("GetRegistryFourStateCounts")
        .WithSummary("Four-state counts (Registered, NeedsReview, NoMatch, Failed) with trigger breakdown.")
        .Produces<RegistryFourStateCounts>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // ── POST /registry/items/{entityId}/apply-match ────────────────────
        group.MapPost("/items/{entityId}/apply-match", async (
            Guid entityId,
            ApplyMatchRequest request,
            IMetadataClaimRepository claimRepo,
            IHydrationPipelineService pipeline,
            IHubRepository hubRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Mode))
                return Results.BadRequest("Mode is required ('Universe' or 'Retail').");

            // Resolve asset ID from work ID
            string? assetIdStr = null;
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
                cmd.Parameters.AddWithValue("@workId", entityId.ToString());
                assetIdStr = cmd.ExecuteScalar()?.ToString();
            }

            if (assetIdStr is null)
                return Results.NotFound($"No media asset found for work {entityId}.");

            if (!Guid.TryParse(assetIdStr, out var assetId))
                return Results.Problem("Invalid asset ID in database.");

            var now = DateTimeOffset.UtcNow;
            var claims = new List<MetadataClaim>();

            // User provider GUID for user-locked claims
            var userProviderId = new Guid("d0000000-0000-4000-8000-000000000001");

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
            AddClaim("title",        request.Title);
            AddClaim("release_year", request.Year);
            AddClaim("author",       request.Author);
            AddClaim("director",     request.Director);
            AddClaim("description",  request.Description);
            AddClaim("cover_url",    request.CoverUrl);

            bool hydrationTriggered = false;
            string wikidataStatus;

            if (string.Equals(request.Mode, "Universe", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(request.Qid))
            {
                // Universe match: lock the QID and trigger full hydration
                AddClaim("wikidata_qid", request.Qid);
                wikidataStatus = "confirmed";

                if (claims.Count > 0)
                    await claimRepo.InsertBatchAsync(claims, ct);

                // Update work's wikidata_status
                await hubRepo.UpdateWorkWikidataStatusAsync(entityId, "confirmed", ct);

                // Build hints from the claims for the pipeline
                var hints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["wikidata_qid"] = request.Qid,
                };
                if (!string.IsNullOrWhiteSpace(request.Title))   hints["title"]        = request.Title;
                if (!string.IsNullOrWhiteSpace(request.Year))    hints["release_year"] = request.Year;
                if (!string.IsNullOrWhiteSpace(request.Author))  hints["author"]       = request.Author;

                // Trigger full hydration (synchronous, Universe pass)
                try
                {
                    await pipeline.RunSynchronousAsync(new HarvestRequest
                    {
                        EntityId   = assetId,
                        EntityType = EntityType.MediaAsset,
                        MediaType  = MediaType.Unknown,
                        Hints      = hints,
                        Pass       = HydrationPass.Universe,
                    }, ct);
                    hydrationTriggered = true;
                }
                catch (Exception)
                {
                    // Hydration failure doesn't fail the match — claims are already written
                    hydrationTriggered = false;
                }
            }
            else
            {
                // Retail match: write metadata claims, mark as missing universe
                wikidataStatus = "missing";

                if (claims.Count > 0)
                    await claimRepo.InsertBatchAsync(claims, ct);

                await hubRepo.UpdateWorkWikidataStatusAsync(entityId, "missing", ct);

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
                        cmd.Parameters.AddWithValue("@entityId", assetIdStr);
                        cmd.Parameters.AddWithValue("@key",      claim.ClaimKey);
                        cmd.Parameters.AddWithValue("@value",    claim.ClaimValue);
                        cmd.Parameters.AddWithValue("@scoredAt", now.ToString("o"));
                        cmd.ExecuteNonQuery();
                    }
                }
            }

            return Results.Ok(new ApplyMatchResponse
            {
                EntityId           = entityId,
                WikidataStatus     = wikidataStatus,
                ClaimsWritten      = claims.Count,
                HydrationTriggered = hydrationTriggered,
                Message            = hydrationTriggered
                    ? $"Universe match applied. QID {request.Qid} locked. Full hydration complete."
                    : wikidataStatus == "missing"
                        ? "Retail match applied. Item marked as Missing Universe."
                        : $"Universe match applied. QID {request.Qid} locked.",
            });
        })
        .WithName("ApplyRegistryMatch")
        .WithSummary("Apply a selected Universe (Wikidata) or Retail provider match to a registry item.")
        .Produces<ApplyMatchResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        // ── POST /registry/items/{entityId}/create-manual ─────────────────
        group.MapPost("/items/{entityId}/create-manual", async (
            Guid entityId,
            CreateManualRequest request,
            IMetadataClaimRepository claimRepo,
            IHubRepository hubRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                return Results.BadRequest("Title is required for manual entry.");

            // Resolve asset ID from work ID
            string? assetIdStr = null;
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
                cmd.Parameters.AddWithValue("@workId", entityId.ToString());
                assetIdStr = cmd.ExecuteScalar()?.ToString();
            }

            if (assetIdStr is null)
                return Results.NotFound($"No media asset found for work {entityId}.");

            if (!Guid.TryParse(assetIdStr, out var assetId))
                return Results.Problem("Invalid asset ID in database.");

            var now          = DateTimeOffset.UtcNow;
            var userProvider = new Guid("d0000000-0000-4000-8000-000000000001");
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

            AddClaim("title",        request.Title);
            AddClaim("release_year", request.Year);
            AddClaim("author",       request.Author);
            AddClaim("description",  request.Description);

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
                    cmd.Parameters.AddWithValue("@entityId", assetIdStr);
                    cmd.Parameters.AddWithValue("@key",      claim.ClaimKey);
                    cmd.Parameters.AddWithValue("@value",    claim.ClaimValue);
                    cmd.Parameters.AddWithValue("@scoredAt", now.ToString("o"));
                    cmd.ExecuteNonQuery();
                }
            }

            await hubRepo.UpdateWorkWikidataStatusAsync(entityId, "manual", ct);

            return Results.Ok(new CreateManualResponse
            {
                EntityId       = entityId,
                WikidataStatus = "manual",
                ClaimsWritten  = claims.Count,
                Message        = $"Manual entry created with {claims.Count} user-locked fields. Item marked for future Universe sweep.",
            });
        })
        .WithName("CreateManualRegistryEntry")
        .WithSummary("Create a manual metadata entry for a registry item with no provider match.")
        .Produces<CreateManualResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        // ── DELETE /registry/items/{entityId} ───────────────────────────────
        group.MapDelete("/items/{entityId}", async (
            Guid entityId,
            IDatabaseConnection db,
            ISystemActivityRepository activityRepo,
            CancellationToken ct) =>
        {
            // 1. Resolve all media asset file paths for this work
            var filePaths = new List<string>();
            string? hubId = null;
            string? workTitle = null;

            using (var conn = db.CreateConnection())
            {
                // Get file paths and hub ID
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = """
                        SELECT ma.file_path_root
                        FROM editions e
                        INNER JOIN media_assets ma ON ma.edition_id = e.id
                        WHERE e.work_id = @workId
                        """;
                    cmd.Parameters.AddWithValue("@workId", entityId.ToString());
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var path = reader.GetString(0);
                        if (!string.IsNullOrWhiteSpace(path))
                            filePaths.Add(path);
                    }
                }

                // Get hub ID for cleanup check
                using (var cmd2 = conn.CreateCommand())
                {
                    cmd2.CommandText = "SELECT hub_id, title FROM works WHERE id = @workId";
                    cmd2.Parameters.AddWithValue("@workId", entityId.ToString());
                    using var reader2 = cmd2.ExecuteReader();
                    if (reader2.Read())
                    {
                        hubId = reader2.IsDBNull(0) ? null : reader2.GetString(0);
                        workTitle = reader2.IsDBNull(1) ? null : reader2.GetString(1);
                    }
                }
            }

            if (filePaths.Count == 0)
                return Results.NotFound($"No media assets found for work {entityId}.");

            // 2. Delete physical files from disk + cover.jpg in same directory
            foreach (var filePath in filePaths)
            {
                try
                {
                    if (File.Exists(filePath))
                        File.Delete(filePath);

                    // Delete cover.jpg in the same directory
                    var dir = Path.GetDirectoryName(filePath);
                    if (dir is not null)
                    {
                        var coverPath = Path.Combine(dir, "cover.jpg");
                        if (File.Exists(coverPath))
                            File.Delete(coverPath);

                        var heroPath = Path.Combine(dir, "hero.jpg");
                        if (File.Exists(heroPath))
                            File.Delete(heroPath);

                        // Remove empty directory
                        if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                            Directory.Delete(dir);
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
                cmd.Parameters.AddWithValue("@workId", entityId.ToString());
                cmd.ExecuteNonQuery();
            }

            // 4. Delete the work (CASCADE handles editions → media_assets → claims → canonical_values)
            using (var conn = db.CreateConnection())
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM works WHERE id = @workId";
                cmd.Parameters.AddWithValue("@workId", entityId.ToString());
                cmd.ExecuteNonQuery();
            }

            // 5. Clean up hub if no remaining works
            if (hubId is not null)
            {
                using var conn = db.CreateConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    DELETE FROM hubs WHERE id = @hubId
                    AND NOT EXISTS (SELECT 1 FROM works WHERE hub_id = @hubId)
                    """;
                cmd.Parameters.AddWithValue("@hubId", hubId);
                cmd.ExecuteNonQuery();
            }

            // 6. Log activity
            await activityRepo.LogAsync(new SystemActivityEntry
            {
                OccurredAt  = DateTimeOffset.UtcNow,
                ActionType  = SystemActionType.MediaRemoved,
                HubName     = workTitle,
                EntityId    = entityId,
                EntityType  = "Work",
                Detail      = $"Removed '{workTitle ?? "unknown"}' — {filePaths.Count} file(s) deleted from disk and database.",
            }, ct);

            return Results.Ok(new { EntityId = entityId, FilesDeleted = filePaths.Count, Message = $"Removed '{workTitle ?? "unknown"}' and {filePaths.Count} file(s)." });
        })
        .WithName("DeleteRegistryItem")
        .WithSummary("Permanently remove a work and all its files from the library.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        // ── POST /registry/items/{entityId}/reject ────────────────────────────
        group.MapPost("/items/{entityId}/reject", async (
            Guid entityId,
            ISystemActivityRepository activityRepo,
            IReviewQueueRepository reviewRepo,
            IDatabaseConnection db,
            IStorageManifest manifest,
            CancellationToken ct) =>
        {
            // Resolve the media asset's current file path and its ID.
            string? assetIdStr = null;
            string? currentFilePath = null;
            string? hubId = null;
            string? workTitle = null;

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
                    cmd.Parameters.AddWithValue("@workId", entityId.ToString());
                    using var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        assetIdStr      = reader.GetString(0);
                        currentFilePath = reader.IsDBNull(1) ? null : reader.GetString(1);
                    }
                }

                using (var cmd2 = conn.CreateCommand())
                {
                    cmd2.CommandText = "SELECT hub_id, title FROM works WHERE id = @workId";
                    cmd2.Parameters.AddWithValue("@workId", entityId.ToString());
                    using var reader2 = cmd2.ExecuteReader();
                    if (reader2.Read())
                    {
                        hubId      = reader2.IsDBNull(0) ? null : reader2.GetString(0);
                        workTitle  = reader2.IsDBNull(1) ? null : reader2.GetString(1);
                    }
                }
            }

            if (assetIdStr is null || currentFilePath is null)
                return Results.NotFound($"No media asset found for work {entityId}.");

            if (!Guid.TryParse(assetIdStr, out var assetId))
                return Results.Problem("Invalid asset ID in database.");

            // Resolve the library root to build the .staging/rejected/ path.
            var core = manifest.Load();
            var libraryRoot = core.LibraryRoot;
            if (string.IsNullOrWhiteSpace(libraryRoot))
                return Results.BadRequest("LibraryRoot is not configured. Cannot determine rejected folder.");

            var rejectedDir = Path.Combine(libraryRoot, ".staging", "rejected");
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
                cmd.Parameters.AddWithValue("@id",   assetIdStr);
                cmd.ExecuteNonQuery();
            }

            // Dismiss pending review items and insert a new Rejected entry.
            using (var conn = db.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    UPDATE review_queue
                    SET status = 'Dismissed', resolved_at = @now, resolved_by = 'user:reject'
                    WHERE entity_id = @assetId AND status = 'Pending'
                    """;
                cmd.Parameters.AddWithValue("@assetId", assetIdStr);
                cmd.Parameters.AddWithValue("@now",     DateTimeOffset.UtcNow.ToString("o"));
                cmd.ExecuteNonQuery();
            }

            // Insert a Rejected review queue entry so the item shows as "Rejected" in the Registry.
            using (var conn = db.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    INSERT INTO review_queue
                        (id, entity_id, entity_type, trigger, status, confidence_score, detail, created_at)
                    VALUES
                        (@id, @entityId, 'MediaAsset', @trigger, 'Pending', 0, @detail, @createdAt)
                    """;
                cmd.Parameters.AddWithValue("@id",        Guid.NewGuid().ToString());
                cmd.Parameters.AddWithValue("@entityId",  assetIdStr);
                cmd.Parameters.AddWithValue("@trigger",   ReviewTrigger.Rejected);
                cmd.Parameters.AddWithValue("@detail",    $"Rejected by user. File moved to .staging/rejected/.");
                cmd.Parameters.AddWithValue("@createdAt", DateTimeOffset.UtcNow.ToString("o"));
                cmd.ExecuteNonQuery();
            }

            // Log to the activity ledger.
            await activityRepo.LogAsync(new SystemActivityEntry
            {
                OccurredAt = DateTimeOffset.UtcNow,
                ActionType = SystemActionType.FileRejected,
                HubName    = workTitle,
                EntityId   = entityId,
                EntityType = "Work",
                Detail     = $"Rejected '{workTitle ?? "unknown"}' — file moved to .staging/rejected/.",
            }, ct);

            return Results.Ok(new { EntityId = entityId, NewFilePath = newFilePath, Message = $"Rejected '{workTitle ?? "unknown"}'." });
        })
        .WithName("RejectRegistryItem")
        .WithSummary("Reject a registry item: move its file to .staging/rejected/ and mark it as Rejected.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        // ── POST /registry/batch/approve ──────────────────────────────────────
        group.MapPost("/batch/approve", async (
            BatchRegistryRequest request,
            IMetadataClaimRepository claimRepo,
            IHubRepository hubRepo,
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
                    await hubRepo.UpdateWorkWikidataStatusAsync(entityId, "missing", ct);

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
                    cmd.Parameters.AddWithValue("@workId", entityId.ToString());
                    cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("o"));
                    cmd.ExecuteNonQuery();

                    processed++;
                }
                catch { /* Skip failed items */ }
            }

            return Results.Ok(new BatchRegistryResponse
            {
                ProcessedCount  = processed,
                TotalRequested  = request.EntityIds.Length,
                Message         = $"Approved {processed} of {request.EntityIds.Length} items.",
            });
        })
        .WithName("BatchApproveRegistryItems")
        .WithSummary("Approve multiple registry items in batch (marks as missing universe, dismisses reviews).")
        .Produces<BatchRegistryResponse>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // ── POST /registry/batch/delete ───────────────────────────────────────
        group.MapPost("/batch/delete", async (
            BatchRegistryRequest request,
            IDatabaseConnection db,
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
                    var filePaths  = new List<string>();
                    string? hubId      = null;
                    string? workTitle  = null;

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
                            cmd.Parameters.AddWithValue("@workId", entityId.ToString());
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
                            cmd2.CommandText = "SELECT hub_id, title FROM works WHERE id = @workId";
                            cmd2.Parameters.AddWithValue("@workId", entityId.ToString());
                            using var reader2 = cmd2.ExecuteReader();
                            if (reader2.Read())
                            {
                                hubId     = reader2.IsDBNull(0) ? null : reader2.GetString(0);
                                workTitle = reader2.IsDBNull(1) ? null : reader2.GetString(1);
                            }
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
                                var coverPath = Path.Combine(dir, "cover.jpg");
                                if (File.Exists(coverPath)) File.Delete(coverPath);
                                var heroPath = Path.Combine(dir, "hero.jpg");
                                if (File.Exists(heroPath)) File.Delete(heroPath);
                                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                                    Directory.Delete(dir);
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
                        cmd.Parameters.AddWithValue("@workId", entityId.ToString());
                        cmd.ExecuteNonQuery();
                    }

                    using (var conn = db.CreateConnection())
                    {
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = "DELETE FROM works WHERE id = @workId";
                        cmd.Parameters.AddWithValue("@workId", entityId.ToString());
                        cmd.ExecuteNonQuery();
                    }

                    if (hubId is not null)
                    {
                        using var conn = db.CreateConnection();
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = """
                            DELETE FROM hubs WHERE id = @hubId
                            AND NOT EXISTS (SELECT 1 FROM works WHERE hub_id = @hubId)
                            """;
                        cmd.Parameters.AddWithValue("@hubId", hubId);
                        cmd.ExecuteNonQuery();
                    }

                    await activityRepo.LogAsync(new SystemActivityEntry
                    {
                        OccurredAt = DateTimeOffset.UtcNow,
                        ActionType = SystemActionType.MediaRemoved,
                        HubName    = workTitle,
                        EntityId   = entityId,
                        EntityType = "Work",
                        Detail     = $"Batch removed '{workTitle ?? "unknown"}' — {filePaths.Count} file(s) deleted.",
                    }, ct);

                    processed++;
                }
                catch { /* Skip failed items */ }
            }

            return Results.Ok(new BatchRegistryResponse
            {
                ProcessedCount  = processed,
                TotalRequested  = request.EntityIds.Length,
                Message         = $"Deleted {processed} of {request.EntityIds.Length} items ({filesRemoved} files removed).",
            });
        })
        .WithName("BatchDeleteRegistryItems")
        .WithSummary("Permanently delete multiple registry items and their files in batch.")
        .Produces<BatchRegistryResponse>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // ── POST /registry/batch/reject ───────────────────────────────────────
        group.MapPost("/batch/reject", async (
            BatchRegistryRequest request,
            IDatabaseConnection db,
            ISystemActivityRepository activityRepo,
            IStorageManifest manifest,
            CancellationToken ct) =>
        {
            if (request.EntityIds is null || request.EntityIds.Length == 0)
                return Results.BadRequest("No entity IDs provided.");

            var core = manifest.Load();
            var libraryRoot = core.LibraryRoot;
            if (string.IsNullOrWhiteSpace(libraryRoot))
                return Results.BadRequest("LibraryRoot is not configured. Cannot determine rejected folder.");

            var rejectedDir = Path.Combine(libraryRoot, ".staging", "rejected");
            Directory.CreateDirectory(rejectedDir);

            int processed = 0;

            foreach (var entityId in request.EntityIds)
            {
                try
                {
                    string? assetIdStr      = null;
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
                            cmd.Parameters.AddWithValue("@workId", entityId.ToString());
                            using var reader = cmd.ExecuteReader();
                            if (reader.Read())
                            {
                                assetIdStr      = reader.GetString(0);
                                currentFilePath = reader.IsDBNull(1) ? null : reader.GetString(1);
                            }
                        }

                        using (var cmd2 = conn.CreateCommand())
                        {
                            cmd2.CommandText = "SELECT title FROM works WHERE id = @workId";
                            cmd2.Parameters.AddWithValue("@workId", entityId.ToString());
                            using var reader2 = cmd2.ExecuteReader();
                            if (reader2.Read())
                                workTitle = reader2.IsDBNull(0) ? null : reader2.GetString(0);
                        }
                    }

                    if (assetIdStr is null || currentFilePath is null) continue;
                    if (!Guid.TryParse(assetIdStr, out var assetId)) continue;

                    var fileName    = Path.GetFileName(currentFilePath);
                    var newFilePath = Path.Combine(rejectedDir, fileName);

                    if (File.Exists(newFilePath) && !string.Equals(currentFilePath, newFilePath, StringComparison.OrdinalIgnoreCase))
                        newFilePath = Path.Combine(rejectedDir, $"{Path.GetFileNameWithoutExtension(fileName)}__{assetId}{Path.GetExtension(fileName)}");

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
                        cmd.Parameters.AddWithValue("@id",   assetIdStr);
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
                        cmd.Parameters.AddWithValue("@assetId", assetIdStr);
                        cmd.Parameters.AddWithValue("@now",     DateTimeOffset.UtcNow.ToString("o"));
                        cmd.ExecuteNonQuery();
                    }

                    using (var conn = db.CreateConnection())
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = """
                            INSERT INTO review_queue
                                (id, entity_id, entity_type, trigger, status, confidence_score, detail, created_at)
                            VALUES
                                (@id, @entityId, 'MediaAsset', @trigger, 'Pending', 0, @detail, @createdAt)
                            """;
                        cmd.Parameters.AddWithValue("@id",        Guid.NewGuid().ToString());
                        cmd.Parameters.AddWithValue("@entityId",  assetIdStr);
                        cmd.Parameters.AddWithValue("@trigger",   ReviewTrigger.Rejected);
                        cmd.Parameters.AddWithValue("@detail",    $"Rejected by user (batch). File moved to .staging/rejected/.");
                        cmd.Parameters.AddWithValue("@createdAt", DateTimeOffset.UtcNow.ToString("o"));
                        cmd.ExecuteNonQuery();
                    }

                    await activityRepo.LogAsync(new SystemActivityEntry
                    {
                        OccurredAt = DateTimeOffset.UtcNow,
                        ActionType = SystemActionType.FileRejected,
                        HubName    = workTitle,
                        EntityId   = entityId,
                        EntityType = "Work",
                        Detail     = $"Batch rejected '{workTitle ?? "unknown"}' — file moved to .staging/rejected/.",
                    }, ct);

                    processed++;
                }
                catch { /* Skip failed items */ }
            }

            return Results.Ok(new BatchRegistryResponse
            {
                ProcessedCount = processed,
                TotalRequested = request.EntityIds.Length,
                Message        = $"Rejected {processed} of {request.EntityIds.Length} items.",
            });
        })
        .WithName("BatchRejectRegistryItems")
        .WithSummary("Reject multiple registry items in batch: move files to .staging/rejected/ and mark as Rejected.")
        .Produces<BatchRegistryResponse>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // ── POST /registry/items/{entityId}/recover ───────────────────────────
        group.MapPost("/items/{entityId:guid}/recover", async (
            Guid entityId,
            IReviewQueueRepository reviewRepo,
            ISystemActivityRepository activityRepo,
            CancellationToken ct) =>
        {
            // Find the rejected review entry(s) and dismiss them.
            var entries = await reviewRepo.GetPendingByEntityAsync(entityId, ct);
            foreach (var entry in entries)
            {
                if (entry.Trigger == ReviewTrigger.Rejected)
                {
                    await reviewRepo.UpdateStatusAsync(entry.Id, ReviewStatus.Dismissed, "user:recover", ct);
                }
            }

            try
            {
                await activityRepo.LogAsync(new SystemActivityEntry
                {
                    ActionType = "Recovered",
                    EntityId = entityId,
                    Detail = "Recovered from rejected state",
                }, ct);
            }
            catch (Exception) { /* history is supplementary */ }

            return Results.Ok(new { message = "Item recovered" });
        })
        .WithName("RecoverRegistryItem")
        .WithSummary("Recover a previously rejected registry item — removes the Rejected status.")
        .Produces(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // ── GET /registry/items/{entityId}/history ────────────────────────────
        // entityId is a work ID from the registry listing. system_activity entries
        // may reference the work ID or the asset ID, so we query for both.
        group.MapGet("/items/{entityId:guid}/history", async (
            Guid entityId,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            // Resolve asset ID from work ID so we can find events logged against either.
            string? assetIdStr;
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
                cmd.Parameters.AddWithValue("@workId", entityId.ToString());
                assetIdStr = cmd.ExecuteScalar()?.ToString();
            }

            // Query system_activity for both work ID and asset ID.
            var workIdStr = entityId.ToString();
            var assetId = assetIdStr ?? workIdStr;

            using var conn2 = db.CreateConnection();
            using var cmd2 = conn2.CreateCommand();
            cmd2.CommandText = """
                SELECT id, occurred_at, action_type, entity_id, detail
                FROM system_activity
                WHERE entity_id = @workId OR entity_id = @assetId
                ORDER BY occurred_at DESC
                LIMIT 200
                """;
            cmd2.Parameters.AddWithValue("@workId", workIdStr);
            cmd2.Parameters.AddWithValue("@assetId", assetId);

            var entries = new List<object>();
            using var reader = cmd2.ExecuteReader();
            while (reader.Read())
            {
                entries.Add(new
                {
                    id = reader.GetInt64(0).ToString(),
                    entity_id = reader.IsDBNull(3) ? entityId : (Guid.TryParse(reader.GetString(3), out var eid) ? eid : entityId),
                    occurred_at = reader.IsDBNull(1) ? DateTimeOffset.UtcNow : DateTimeOffset.Parse(reader.GetString(1)),
                    event_type = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    label = reader.IsDBNull(2) ? "" : FormatActionTypeLabel(reader.GetString(2)),
                    detail = reader.IsDBNull(4) ? (string?)null : reader.GetString(4),
                });
            }

            return Results.Ok(entries);
        })
        .WithName("GetRegistryItemHistory")
        .WithSummary("Get processing history timeline for a registry item")
        .Produces(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        return app;
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
        "HubCreated"               => "Hub created",
        "HubAssigned"              => "Assigned to hub",
        "PersonHydrated"           => "Person enriched",
        "FileRejected"             => "Rejected",
        "Recovered"                => "Recovered from rejection",
        "FileHashed"               => "Content fingerprinted",
        "DuplicateSkipped"         => "Duplicate skipped",
        "EntityChainCreated"       => "Library records created",
        "HydrationEnqueued"        => "Queued for enrichment",
        _                          => actionType.Replace("_", " "),
    };
}
