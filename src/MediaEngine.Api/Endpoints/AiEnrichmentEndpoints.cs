using MediaEngine.Api.Http;
using MediaEngine.Api.Security;
using MediaEngine.Contracts.Ai;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Models;

namespace MediaEngine.Api.Endpoints;

/// <summary>
/// AI enrichment API endpoints — TL;DR generation, vibe tagging, intent search,
/// and URL metadata extraction.
///
/// These endpoints expose the AI feature pipeline to Dashboard and third-party clients.
/// </summary>
internal static class AiEnrichmentEndpoints
{
    internal static RouteGroupBuilder MapAiEnrichmentEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/ai/enrich")
                          .WithTags("AI Enrichment");

        // ── GET /ai/enrich/tldr/{entityId} ───────────────────────────────────
        group.MapGet("/tldr/{entityId:guid}", async (
            Guid entityId,
            ITldrGenerator tldr,
            ICanonicalValueRepository canonicalRepo,
            CancellationToken ct) =>
        {
            var canonicals = await canonicalRepo.GetByEntityAsync(entityId, ct);

            // Return cached TL;DR if already generated.
            var existing = canonicals
                .FirstOrDefault(c => string.Equals(c.Key, "tldr", StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
                return Results.Ok(new TldrResponse(existing.Value));

            // Need a description to summarize.
            var description = canonicals
                .FirstOrDefault(c => string.Equals(c.Key, "description", StringComparison.OrdinalIgnoreCase));
            if (description is null)
                return ApiErrors.NotFound("No description available to summarize.");

            var summary = await tldr.SummarizeAsync(description.Value, ct);
            return summary is not null
                ? Results.Ok(new TldrResponse(summary))
                : Results.Ok(new TldrUnavailableResponse(tldr: null, note: "Could not generate summary."));
        })
        .WithName("GetTldr")
        .WithSummary("Generate or fetch a one-sentence TL;DR summary for an entity.")
        .Produces<TldrResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        // ── GET /ai/enrich/vibes/{entityId} ──────────────────────────────────
        group.MapGet("/vibes/{entityId:guid}", async (
            Guid entityId,
            IVibeTagger tagger,
            ICanonicalValueRepository canonicalRepo,
            CancellationToken ct) =>
        {
            var canonicals = await canonicalRepo.GetByEntityAsync(entityId, ct);

            // Return cached vibe tags if already generated.
            var existingVibes = canonicals
                .Where(c => string.Equals(c.Key, "vibe", StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Value)
                .ToList();

            if (existingVibes.Count > 0)
                return Results.Ok(new VibesResponse(existingVibes));

            // Generate vibes from description + genre + media type.
            var description = canonicals.FirstOrDefault(c =>
                string.Equals(c.Key, "description", StringComparison.OrdinalIgnoreCase));
            var genres = canonicals
                .Where(c => string.Equals(c.Key, "genre", StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Value)
                .ToList();
            var mediaType = canonicals.FirstOrDefault(c =>
                string.Equals(c.Key, "media_type", StringComparison.OrdinalIgnoreCase));

            var tags = await tagger.TagAsync(
                entityId.ToString(), description?.Value, genres,
                mediaType?.Value ?? "unknown", ct);

            return Results.Ok(new VibesResponse(tags));
        })
        .WithName("GetVibes")
        .WithSummary("Generate or fetch vibe/mood tags for an entity.")
        .Produces<VibesResponse>(StatusCodes.Status200OK)
        .RequireAnyRole();

        // ── POST /ai/enrich/search/intent ────────────────────────────────────
        group.MapPost("/search/intent", async (
            IntentSearchRequest request,
            IIntentSearchParser parser,
            CancellationToken ct) =>
        {
            var result = await parser.ParseAsync(request.Query, ct);
            return Results.Ok(AiContractMapper.ToContract(result));
        })
        .WithName("IntentSearch")
        .WithSummary("Parse a natural language search query into structured filters.")
        .Produces<IntentSearchResponse>(StatusCodes.Status200OK)
        .RequireAnyRole();

        // ── POST /ai/enrich/extract-url ───────────────────────────────────────
        group.MapPost("/extract-url", async (
            UrlExtractRequest request,
            IUrlMetadataExtractor extractor,
            CancellationToken ct) =>
        {
            var result = await extractor.ExtractAsync(request.Url, ct);
            return result.Success
                ? Results.Ok(AiContractMapper.ToContract(result))
                : ApiErrors.BadRequest(result.ErrorMessage ?? "Failed to extract metadata from the URL.");
        })
        .WithName("ExtractUrlMetadata")
        .WithSummary("Extract structured metadata from a URL using AI. Requires Curator or Administrator role.")
        .Produces<UrlExtractionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAdminOrStandardUser();

        return group;
    }
}
