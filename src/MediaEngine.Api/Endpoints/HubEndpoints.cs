using MediaEngine.Api.Models;
using MediaEngine.Api.Security;
using MediaEngine.Domain;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Endpoints;

public static class HubEndpoints
{
    public static IEndpointRouteBuilder MapHubEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/hubs")
                       .WithTags("Hubs");

        group.MapGet("/", async (
            IHubRepository hubRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            var hubs = await hubRepo.GetAllAsync(ct);

            // Collect work IDs that have at least one non-staging media asset
            var libraryWorkIds = new HashSet<Guid>();
            using (var conn = db.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT DISTINCT e.work_id
                    FROM editions e
                    INNER JOIN media_assets ma ON ma.edition_id = e.id
                    WHERE ma.file_path_root NOT LIKE '%/.staging/%'
                      AND ma.file_path_root NOT LIKE '%\.staging\%'
                      AND ma.file_path_root NOT LIKE '%/.staging\%'
                    """;
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    if (Guid.TryParse(reader.GetString(0), out var wid))
                        libraryWorkIds.Add(wid);
                }
            }

            // Filter hubs: only include works that are in the library (not staging)
            var filtered = new List<HubDto>();
            foreach (var hub in hubs)
            {
                var libraryWorks = hub.Works.Where(w => libraryWorkIds.Contains(w.Id)).ToList();
                if (libraryWorks.Count == 0) continue;

                var filteredHub = new Hub
                {
                    Id             = hub.Id,
                    UniverseId     = hub.UniverseId,
                    DisplayName    = hub.DisplayName,
                    CreatedAt      = hub.CreatedAt,
                    UniverseStatus = hub.UniverseStatus,
                    ParentHubId    = hub.ParentHubId,
                    WikidataQid    = hub.WikidataQid,
                };
                foreach (var w in libraryWorks)         filteredHub.Works.Add(w);
                foreach (var r in hub.Relationships)    filteredHub.Relationships.Add(r);

                filtered.Add(HubDto.FromDomain(filteredHub));
            }

            return Results.Ok(filtered);
        })
        .WithName("GetAllHubs")
        .WithSummary("List all media hubs with their works and canonical metadata values.")
        .Produces<List<HubDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        group.MapGet("/search", async (
            string? q,
            IHubRepository hubRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
                return Results.Ok(Array.Empty<SearchResultDto>());

            var query = q.Trim();
            var hubs  = await hubRepo.GetAllAsync(ct);

            // Collect work IDs that have at least one non-staging media asset
            var libraryWorkIds = new HashSet<Guid>();
            using (var conn = db.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT DISTINCT e.work_id
                    FROM editions e
                    INNER JOIN media_assets ma ON ma.edition_id = e.id
                    WHERE ma.file_path_root NOT LIKE '%/.staging/%'
                      AND ma.file_path_root NOT LIKE '%\.staging\%'
                      AND ma.file_path_root NOT LIKE '%/.staging\%'
                    """;
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    if (Guid.TryParse(reader.GetString(0), out var wid))
                        libraryWorkIds.Add(wid);
                }
            }

            // Build DTOs for library-only works
            var filtered = new List<HubDto>();
            foreach (var hub in hubs)
            {
                var libraryWorks = hub.Works.Where(w => libraryWorkIds.Contains(w.Id)).ToList();
                if (libraryWorks.Count == 0) continue;

                var filteredHub = new Hub
                {
                    Id             = hub.Id,
                    UniverseId     = hub.UniverseId,
                    DisplayName    = hub.DisplayName,
                    CreatedAt      = hub.CreatedAt,
                    UniverseStatus = hub.UniverseStatus,
                    ParentHubId    = hub.ParentHubId,
                    WikidataQid    = hub.WikidataQid,
                };
                foreach (var w in libraryWorks)         filteredHub.Works.Add(w);
                foreach (var r in hub.Relationships)    filteredHub.Relationships.Add(r);

                filtered.Add(HubDto.FromDomain(filteredHub));
            }

            var dtos = filtered;

            var results = dtos
                .SelectMany(hub => hub.Works
                    .Where(w => WorkMatchesQuery(w, query))
                    .Select(w => new SearchResultDto
                    {
                        WorkId          = w.Id,
                        HubId           = hub.Id,
                        Title           = GetCanonical(w, "title")   ?? $"Work {w.Id}",
                        Author          = GetCanonical(w, "author"),
                        MediaType       = w.MediaType,
                        HubDisplayName  = GetCanonical(hub.Works.FirstOrDefault()!, "title")
                                          ?? hub.Id.ToString("N")[..8],
                        CoverUrl        = GetCanonical(w, "cover"),
                    }))
                .Take(20)
                .ToList();

            return Results.Ok(results);
        })
        .WithName("SearchHubs")
        .WithSummary("Full-text search across all works. Returns up to 20 matching results.")
        .Produces<List<SearchResultDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();


        // GET /hubs/{id}/related?limit= — cascading related hubs: series → author → genre → explore.
        // GET /hubs/parents — list all Parent Hubs for top-level franchise navigation.
        // IMPORTANT: registered before /{id:guid} routes to avoid route conflicts.
        group.MapGet("/parents", async (IHubRepository hubRepo, CancellationToken ct) =>
        {
            var allHubs = await hubRepo.GetAllAsync(ct);

            var parentIds = allHubs
                .Where(h => h.ParentHubId.HasValue)
                .Select(h => h.ParentHubId!.Value)
                .Distinct()
                .ToHashSet();

            var parents = allHubs
                .Where(h => parentIds.Contains(h.Id))
                .Select(h => new
                {
                    id             = h.Id,
                    displayName    = h.DisplayName,
                    childCount     = allHubs.Count(c => c.ParentHubId == h.Id),
                    createdAt      = h.CreatedAt,
                    universeStatus = h.UniverseStatus,
                })
                .OrderBy(h => h.displayName)
                .ToList();

            return Results.Ok(parents);
        })
        .WithName("GetParentHubs")
        .WithSummary("Returns all Parent Hubs (franchise-level groupings).")
        .RequireAnyRole();

        // GET /hubs/{id}/children — returns child Hubs of a given parent.
        group.MapGet("/{id:guid}/children", async (Guid id, IHubRepository hubRepo, CancellationToken ct) =>
        {
            var children = await hubRepo.GetChildHubsAsync(id, ct);
            var result = children.Select(h => new
            {
                id             = h.Id,
                displayName    = h.DisplayName,
                parentHubId    = h.ParentHubId,
                createdAt      = h.CreatedAt,
                universeStatus = h.UniverseStatus,
            }).ToList();

            return Results.Ok(result);
        })
        .WithName("GetHubChildren")
        .WithSummary("Returns child Hubs of the given Parent Hub.")
        .RequireAnyRole();

        // GET /hubs/{id}/parent — returns the parent Hub of a given Hub (if any).
        group.MapGet("/{id:guid}/parent", async (Guid id, IHubRepository hubRepo, CancellationToken ct) =>
        {
            var hub = await hubRepo.GetByIdAsync(id, ct);
            if (hub is null)
                return Results.NotFound();

            if (!hub.ParentHubId.HasValue)
                return Results.Ok(new { parentHub = (object?)null });

            var parent = await hubRepo.GetByIdAsync(hub.ParentHubId.Value, ct);
            if (parent is null)
                return Results.Ok(new { parentHub = (object?)null });

            return Results.Ok(new
            {
                parentHub = new
                {
                    id             = parent.Id,
                    displayName    = parent.DisplayName,
                    createdAt      = parent.CreatedAt,
                    universeStatus = parent.UniverseStatus,
                }
            });
        })
        .WithName("GetHubParent")
        .WithSummary("Returns the Parent Hub of the given Hub, if any.")
        .RequireAnyRole();

        group.MapGet("/{id:guid}/related", async (
            Guid id,
            int? limit,
            IHubRepository hubRepo,
            CancellationToken ct) =>
        {
            var allHubs = await hubRepo.GetAllAsync(ct);
            var dtos    = allHubs.Select(HubDto.FromDomain).ToList();

            var target = dtos.FirstOrDefault(h => h.Id == id);
            if (target is null)
                return Results.NotFound($"Hub '{id}' not found.");

            int take = limit is > 0 ? limit.Value : 20;

            var targetSeries = GetCanonical(target.Works.FirstOrDefault(), "series");
            var targetAuthor = GetCanonical(target.Works.FirstOrDefault(), "author");
            var targetGenre  = GetCanonical(target.Works.FirstOrDefault(), "genre");

            var result   = new List<HubDto>();
            var seen     = new HashSet<Guid> { id };
            string reason = "explore";
            string title  = "Explore Your Library";

            // Stage 1: same series
            if (!string.IsNullOrWhiteSpace(targetSeries))
            {
                var matches = dtos
                    .Where(h => !seen.Contains(h.Id) &&
                           string.Equals(GetCanonical(h.Works.FirstOrDefault(), "series"),
                               targetSeries, StringComparison.OrdinalIgnoreCase))
                    .Take(take)
                    .ToList();
                if (matches.Count > 0)
                {
                    result.AddRange(matches);
                    matches.ForEach(h => seen.Add(h.Id));
                    reason = "series";
                    title  = $"More in {targetSeries}";
                }
            }

            // Stage 2: same author
            if (result.Count < take && !string.IsNullOrWhiteSpace(targetAuthor))
            {
                var matches = dtos
                    .Where(h => !seen.Contains(h.Id) &&
                           string.Equals(GetCanonical(h.Works.FirstOrDefault(), "author"),
                               targetAuthor, StringComparison.OrdinalIgnoreCase))
                    .Take(take - result.Count)
                    .ToList();
                if (matches.Count > 0)
                {
                    if (result.Count == 0) { reason = "author"; title = $"More by {targetAuthor}"; }
                    result.AddRange(matches);
                    matches.ForEach(h => seen.Add(h.Id));
                }
            }

            // Stage 3: same genre
            if (result.Count < take && !string.IsNullOrWhiteSpace(targetGenre))
            {
                var targetGenreFirst = targetGenre.Split(',', ';')[0].Trim();
                var matches = dtos
                    .Where(h => !seen.Contains(h.Id) &&
                           (GetCanonical(h.Works.FirstOrDefault(), "genre") ?? string.Empty)
                               .Contains(targetGenreFirst, StringComparison.OrdinalIgnoreCase))
                    .Take(take - result.Count)
                    .ToList();
                if (matches.Count > 0)
                {
                    if (result.Count == 0) { reason = "genre"; title = $"More {targetGenreFirst}"; }
                    result.AddRange(matches);
                    matches.ForEach(h => seen.Add(h.Id));
                }
            }

            // Stage 4: random fill
            if (result.Count < take)
            {
                var fill = dtos
                    .Where(h => !seen.Contains(h.Id))
                    .OrderBy(_ => Guid.NewGuid())
                    .Take(take - result.Count)
                    .ToList();
                result.AddRange(fill);
            }

            return Results.Ok(new RelatedHubsResponse
            {
                SectionTitle = title,
                Reason       = reason,
                Hubs         = result,
            });
        })
        .WithName("GetRelatedHubs")
        .WithSummary("Related hubs via cascade: series → author → genre → explore.")
        .Produces<RelatedHubsResponse>(StatusCodes.Status200OK)
        .RequireAnyRole();

        // ── Managed Hub endpoints (Vault Hubs tab) ──────────────────────────────

        // GET /hubs/managed — all non-Universe hubs for the Vault Hubs tab.
        group.MapGet("/managed", async (
            IHubRepository hubRepo,
            CancellationToken ct) =>
        {
            var hubs = await hubRepo.GetManagedHubsAsync(ct);
            var dtos = new List<ManagedHubDto>();
            foreach (var hub in hubs)
            {
                // For Smart hubs, item count is from works.hub_id; for others, from hub_items
                int count = hub.HubType == "Smart"
                    ? hub.Works.Count
                    : await hubRepo.GetHubItemCountAsync(hub.Id, ct);
                dtos.Add(ManagedHubDto.FromDomain(hub, count));
            }
            return Results.Ok(dtos);
        })
        .WithName("GetManagedHubs")
        .WithSummary("List all non-Universe hubs (Smart, System, Mix, Playlist) for the Vault Hubs tab.")
        .Produces<List<ManagedHubDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        // GET /hubs/managed/counts — type → count for stats bar.
        group.MapGet("/managed/counts", async (
            IHubRepository hubRepo,
            CancellationToken ct) =>
        {
            var counts = await hubRepo.GetCountsByTypeAsync(ct);
            return Results.Ok(counts);
        })
        .WithName("GetManagedHubCounts")
        .WithSummary("Returns hub count grouped by type for the Vault stats bar.")
        .Produces<Dictionary<string, int>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        // GET /hubs/{id}/items?limit=20 — curated item preview.
        group.MapGet("/{id:guid}/items", async (
            Guid id,
            int? limit,
            IHubRepository hubRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            int take = limit is > 0 ? limit.Value : 20;
            var items = await hubRepo.GetHubItemsAsync(id, take, ct);

            // Resolve work metadata from canonical_values
            var dtos = new List<HubItemDto>();
            if (items.Count > 0)
            {
                using var conn = db.CreateConnection();
                foreach (var item in items)
                {
                    string? title = null, creator = null, mediaType = null, cover = null;
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = """
                        SELECT cv.key, cv.value
                        FROM canonical_values cv
                        WHERE cv.entity_id = @WorkId
                          AND cv.key IN ('title', 'author', 'cover')
                        UNION ALL
                        SELECT 'media_type', w.media_type
                        FROM works w WHERE w.id = @WorkId
                        """;
                    var p = cmd.CreateParameter();
                    p.ParameterName = "@WorkId";
                    p.Value = item.WorkId.ToString();
                    cmd.Parameters.Add(p);

                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var key = reader.GetString(0);
                        var val = reader.GetString(1);
                        switch (key)
                        {
                            case "title": title = val; break;
                            case "author": creator = val; break;
                            case "cover": cover = val; break;
                            case "media_type": mediaType = val; break;
                        }
                    }

                    dtos.Add(new HubItemDto
                    {
                        Id        = item.Id,
                        WorkId    = item.WorkId,
                        Title     = title ?? $"Work {item.WorkId.ToString("N")[..8]}",
                        Creator   = creator,
                        MediaType = mediaType ?? "Unknown",
                        CoverUrl  = cover,
                        SortOrder = item.SortOrder,
                    });
                }
            }

            return Results.Ok(dtos);
        })
        .WithName("GetHubItems")
        .WithSummary("Returns curated items for a hub with resolved work metadata.")
        .Produces<List<HubItemDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        // PUT /hubs/{id}/enabled — toggle hub visibility.
        group.MapPut("/{id:guid}/enabled", async (
            Guid id,
            EnabledRequest body,
            IHubRepository hubRepo,
            CancellationToken ct) =>
        {
            await hubRepo.UpdateHubEnabledAsync(id, body.Enabled, ct);
            return Results.Ok();
        })
        .WithName("UpdateHubEnabled")
        .WithSummary("Toggle a hub's enabled state.")
        .RequireAnyRole();

        // PUT /hubs/{id}/featured — toggle hub featured state.
        group.MapPut("/{id:guid}/featured", async (
            Guid id,
            FeaturedRequest body,
            IHubRepository hubRepo,
            CancellationToken ct) =>
        {
            await hubRepo.UpdateHubFeaturedAsync(id, body.Featured, ct);
            return Results.Ok();
        })
        .WithName("UpdateHubFeatured")
        .WithSummary("Toggle a hub's featured state.")
        .RequireAnyRole();

        return app;
    }

    // ── Request bodies ────────────────────────────────────────────────────────

    public sealed record EnabledRequest(bool Enabled);
    public sealed record FeaturedRequest(bool Featured);

    // ── Private helpers ───────────────────────────────────────────────────────

    private static bool WorkMatchesQuery(WorkDto w, string query) =>
        w.CanonicalValues.Any(cv =>
            cv.Value.Contains(query, StringComparison.OrdinalIgnoreCase));

    private static HashSet<string> MultiValuedKeys => MetadataFieldConstants.MultiValuedKeys;

    private static string? GetCanonical(WorkDto? w, string key)
    {
        var raw = w?.CanonicalValues
            .FirstOrDefault(cv => cv.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            ?.Value;
        if (raw is not null && raw.Contains("|||", StringComparison.Ordinal) && !MultiValuedKeys.Contains(key))
            return raw.Split("|||", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return raw;
    }
}
