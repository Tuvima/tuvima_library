using MediaEngine.Api.Models;
using MediaEngine.Api.Security;
using MediaEngine.Domain.Contracts;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Endpoints;

public static class WorkEndpoints
{
    public static IEndpointRouteBuilder MapWorkEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/works")
                       .WithTags("Works");

        group.MapGet("/{workId:guid}/cast", async (
            Guid workId,
            ICanonicalValueArrayRepository canonicalArrayRepo,
            IPersonRepository personRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            var cast = await CastCreditQueries.BuildForWorkAsync(
                workId,
                canonicalArrayRepo,
                personRepo,
                db,
                ct);

            return Results.Ok(cast);
        })
        .WithName("GetWorkCast")
        .WithSummary("Returns actor and character credits for a single work.")
        .Produces<List<CastCreditDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        return app;
    }
}
