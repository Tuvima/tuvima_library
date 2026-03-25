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

        // ── POST /search/resolve ─────────────────────────────────────────────
        group.MapPost("/resolve", async (
            ResolveSearchRequest request,
            ISearchService searchService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Query))
                return Results.BadRequest("Query is required.");

            // Extract local title/author/year from file hints for retail scoring
            request.FileHints.TryGetValue("title",  out var localTitle);
            request.FileHints.TryGetValue("author", out var localAuthor);
            request.FileHints.TryGetValue("year",   out var localYear);

            var retailRequest = new SearchRetailRequest(
                request.Query,
                request.MediaType,
                request.MaxCandidates,
                LocalTitle:  localTitle,
                LocalAuthor: localAuthor,
                LocalYear:   localYear,
                FileHints:   request.FileHints.Count > 0
                                 ? request.FileHints
                                 : null);

            var retailResults = await searchService.SearchRetailAsync(retailRequest, ct);

            // Map retail candidates to resolve candidates.
            // Wikidata bridge resolution runs client-side after the user selects a candidate
            // (too slow to run for all candidates in one request).
            var candidates = retailResults.Candidates
                .Select(r => new ResolveCandidate
                {
                    ProviderName     = r.ProviderName,
                    ProviderItemId   = r.ProviderItemId ?? "",
                    Title            = r.Title,
                    Author           = r.Author,
                    Year             = r.Year,
                    Description      = r.Description,
                    CoverUrl         = r.CoverUrl,
                    RetailScore      = r.Confidence,
                    DescriptionScore = r.DescriptionMatchScore,
                    CompositeScore   = r.CompositeScore,
                    FieldMatches     = r.DescriptionFieldMatches?
                        .Select(f => new FieldMatchDetail
                        {
                            FieldKey  = f.FieldKey,
                            FileValue = f.FileValue,
                            Matched   = f.Matched,
                            RawScore  = f.RawScore,
                            Weight    = f.Weight,
                        })
                        .ToList(),
                })
                .ToList();

            return Results.Ok(new ResolveSearchResponse { Candidates = candidates });
        })
        .WithName("SearchResolve")
        .WithDescription("Unified resolve search: retail identification with description-based scoring. " +
                         "Wikidata bridge resolution runs client-side after candidate selection.")
        .Produces<ResolveSearchResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAdminOrCurator();

        return app;
    }
}
