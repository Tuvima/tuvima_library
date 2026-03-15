using MediaEngine.Domain.Contracts;

namespace MediaEngine.Api.Endpoints;

/// <summary>
/// Canon discrepancy detection endpoints — compares an edition's metadata
/// against its master work's canonical values from Wikidata.
/// </summary>
public static class CanonEndpoints
{
    public static IEndpointRouteBuilder MapCanonEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/")
                       .WithTags("Canon");

        // GET /metadata/{entityId}/canon-discrepancies — detect field-level mismatches.
        group.MapGet("/metadata/{entityId:guid}/canon-discrepancies", async (
            Guid entityId,
            ICanonDiscrepancyService canonService,
            CancellationToken ct) =>
        {
            var discrepancies = await canonService.DetectAsync(entityId, ct);
            return Results.Ok(discrepancies);
        });

        return app;
    }
}
