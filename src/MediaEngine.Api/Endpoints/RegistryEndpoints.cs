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
                DuplicatesOnly: duplicatesOnly ?? false);

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
        .WithSummary("Status counts for tab badges (All, Review, Auto, Edited, Duplicate).")
        .Produces<RegistryStatusCounts>(StatusCodes.Status200OK)
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

        return app;
    }
}
