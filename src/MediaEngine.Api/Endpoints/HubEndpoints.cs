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
            var dtos = hubs.Select(HubDto.FromDomain).ToList();
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
            var dtos  = hubs.Select(HubDto.FromDomain).ToList();

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
                    }))
                .Take(20)
                .ToList();

            return Results.Ok(results);
        })
        .WithName("SearchHubs")
        .WithSummary("Full-text search across all works. Returns up to 20 matching results.")
        .Produces<List<SearchResultDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        return app;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static bool WorkMatchesQuery(WorkDto w, string query) =>
        w.CanonicalValues.Any(cv =>
            cv.Value.Contains(query, StringComparison.OrdinalIgnoreCase));

    private static string? GetCanonical(WorkDto? w, string key) =>
        w?.CanonicalValues
          .FirstOrDefault(cv => cv.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
          ?.Value;
}
