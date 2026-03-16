using MediaEngine.Api.Models;
using MediaEngine.Api.Security;
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
            CancellationToken ct) =>
        {
            var hubs = await hubRepo.GetAllAsync(ct);
            var dtos = hubs.Where(h => h.Works.Count > 0).Select(HubDto.FromDomain).ToList();
            return Results.Ok(dtos);
        })
        .WithName("GetAllHubs")
        .WithSummary("List all media hubs with their works and canonical metadata values.")
        .Produces<List<HubDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        group.MapGet("/search", async (
            string? q,
            IHubRepository hubRepo,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
                return Results.Ok(Array.Empty<SearchResultDto>());

            var query = q.Trim();
            var hubs  = await hubRepo.GetAllAsync(ct);
            var dtos  = hubs.Where(h => h.Works.Count > 0).Select(HubDto.FromDomain).ToList();

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
        return app;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static bool WorkMatchesQuery(WorkDto w, string query) =>
        w.CanonicalValues.Any(cv =>
            cv.Value.Contains(query, StringComparison.OrdinalIgnoreCase));

    private static readonly HashSet<string> MultiValuedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "genre", "characters", "cast_member", "voice_actor",
        "narrative_location", "main_subject", "composer", "screenwriter",
    };

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
