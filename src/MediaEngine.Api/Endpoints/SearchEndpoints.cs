using MediaEngine.Api.Security;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Models;

namespace MediaEngine.Api.Endpoints;

/// <summary>
/// Search endpoints for Universe (Wikidata) and Retail provider searches.
/// Used by the Registry's MediaSearchPanel to find matches for items.
/// </summary>
public static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/search")
                       .WithTags("Search");

        // ── POST /search/universe ────────────────────────────────────────────
        group.MapPost("/universe", async (
            SearchUniverseRequest request,
            ISearchService searchService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Query))
                return Results.BadRequest("Query is required.");

            var result = await searchService.SearchUniverseAsync(request, ct);
            return Results.Ok(result);
        })
        .WithName("SearchUniverse")
        .WithSummary("Search Wikidata for identity candidates, enriched with cover art from retail providers.")
        .Produces<SearchUniverseResult>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAdminOrCurator();

        // ── POST /search/retail ──────────────────────────────────────────────
        group.MapPost("/retail", async (
            SearchRetailRequest request,
            ISearchService searchService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Query))
                return Results.BadRequest("Query is required.");

            var result = await searchService.SearchRetailAsync(request, ct);
            return Results.Ok(result);
        })
        .WithName("SearchRetail")
        .WithSummary("Search retail providers (TMDB, Apple Books, etc.) for cover art and basic metadata.")
        .Produces<SearchRetailResult>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAdminOrCurator();

        return app;
    }
}
