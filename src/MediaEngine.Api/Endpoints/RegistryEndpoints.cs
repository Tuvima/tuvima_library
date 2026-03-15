using MediaEngine.Api.Security;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Models;

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

        return app;
    }
}
