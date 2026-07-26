using MediaEngine.Api.Http;
using MediaEngine.Api.Security;
using MediaEngine.Contracts.Search;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Models;

namespace MediaEngine.Api.Endpoints;

/// <summary>
/// Search endpoints for Universe (Wikidata) and Retail provider searches.
/// Used by the LibraryItem's MediaSearchPanel to find matches for items.
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
                return ApiErrors.BadRequest("Query is required.");

            var result = await searchService.SearchUniverseAsync(request, ct);
            return Results.Ok(result);
        })
        .WithName("SearchUniverse")
        .WithSummary("Search Wikidata for identity candidates, enriched with cover art from retail providers.")
        .Produces<SearchUniverseResult>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAdminOrCurator();

        // ── POST /search/retail ──────────────────────────────────────────────
        group.MapPost("/retail", async (
            SearchRetailRequestDto request,
            ISearchService searchService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Query))
                return ApiErrors.BadRequest("Query is required.");

            var result = await searchService.SearchRetailAsync(new SearchRetailRequest(
                request.Query,
                request.MediaType,
                request.MaxCandidates,
                request.LocalTitle,
                request.LocalAuthor,
                request.LocalYear,
                request.FileHints,
                request.SearchFields), ct);
            return Results.Ok(MapRetailResponse(result));
        })
        .WithName("SearchRetail")
        .WithSummary("Search retail providers (TMDB, Apple Books, etc.) for cover art and basic metadata.")
        .Produces<SearchRetailResponseDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAdminOrCurator();

        // ── POST /search/resolve ─────────────────────────────────────────────
        group.MapPost("/resolve", async (
            SearchResolveRequestDto request,
            ISearchService searchService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Query))
                return ApiErrors.BadRequest("Query is required.");

            // Extract local title/author/year from file hints for retail scoring
            var fileHints = request.FileHints ?? [];
            fileHints.TryGetValue("title",  out var localTitle);
            fileHints.TryGetValue("author", out var localAuthor);
            fileHints.TryGetValue("year",   out var localYear);

            var retailRequest = new SearchRetailRequest(
                request.Query,
                request.MediaType,
                request.MaxCandidates,
                LocalTitle:  localTitle,
                LocalAuthor: localAuthor,
                LocalYear:   localYear,
                FileHints:   fileHints.Count > 0
                                 ? fileHints
                                 : null);

            var retailResults = await searchService.SearchRetailAsync(retailRequest, ct);

            // Map retail candidates to resolve candidates.
            // Wikidata bridge resolution runs client-side after the user selects a candidate
            // (too slow to run for all candidates in one request).
            var candidates = retailResults.Candidates
                .Select(r => new SearchResolveCandidateDto
                {
                    ProviderName     = r.ProviderName,
                    ProviderItemId   = r.ProviderItemId ?? "",
                    Title            = r.Title,
                    Author           = r.Author,
                    Year             = r.Year,
                    Description      = r.Description,
                    CoverUrl         = r.CoverUrl,
                    RetailScore      = r.Confidence,
                    DescriptionScore = 0.0,
                    CompositeScore   = r.CompositeScore,
                })
                .ToList();

            return Results.Ok(new SearchResolveResponseDto { Candidates = candidates });
        })
        .WithName("SearchResolve")
        .WithDescription("Unified resolve search: retail identification with description-based scoring. " +
                         "Wikidata bridge resolution runs client-side after candidate selection.")
        .Produces<SearchResolveResponseDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAdminOrCurator();

        return app;
    }

    private static SearchRetailResponseDto MapRetailResponse(SearchRetailResult result)
    {
        return new SearchRetailResponseDto
        {
            Query = result.Query,
            MediaType = result.MediaType,
            Candidates = result.Candidates.Select(candidate => new SearchRetailCandidateDto
            {
                ProviderId = candidate.ProviderId,
                ProviderName = candidate.ProviderName,
                ProviderItemId = candidate.ProviderItemId,
                Title = candidate.Title,
                Year = candidate.Year,
                Author = candidate.Author,
                Director = candidate.Director,
                Description = candidate.Description,
                CoverUrl = candidate.CoverUrl,
                Confidence = candidate.Confidence,
                ExtraFields = new Dictionary<string, string>(
                    candidate.ExtraFields,
                    StringComparer.OrdinalIgnoreCase),
                MatchScores = MapMatchScores(candidate.MatchScores),
                CompositeScore = candidate.CompositeScore,
            }).ToList(),
        };
    }

    private static FieldMatchScoresDto? MapMatchScores(FieldMatchResult? scores)
    {
        return scores is null
            ? null
            : new FieldMatchScoresDto
            {
                TitleScore = scores.TitleScore,
                AuthorScore = scores.AuthorScore,
                YearScore = scores.YearScore,
                FormatScore = scores.FormatScore,
                CompositeScore = scores.CompositeScore,
                TitleVerdict = (int)scores.TitleVerdict,
                AuthorVerdict = (int)scores.AuthorVerdict,
                YearVerdict = (int)scores.YearVerdict,
                FormatVerdict = (int)scores.FormatVerdict,
                CoverScore = scores.CoverScore,
                CoverVerdict = (int)scores.CoverVerdict,
            };
    }
}
