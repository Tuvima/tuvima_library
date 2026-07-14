using MediaEngine.Api.Models;
using MediaEngine.Api.Security;
using MediaEngine.Api.Services.ReadServices;

namespace MediaEngine.Api.Endpoints;

public static class WorkEndpoints
{
    public static IEndpointRouteBuilder MapWorkEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/works")
                       .WithTags("Works");

        group.MapGet("/{workId:guid}", async (
            Guid workId,
            IWorkDetailReadService workDetailReadService,
            CancellationToken ct) =>
        {
            var detail = await workDetailReadService.GetAsync(workId, ct);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        })
        .WithName("GetWorkDetail")
        .WithSummary("Returns a single work with canonical values, editions, and owned assets.")
        .Produces<WorkDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapGet("/{workId:guid}/editions", async (
            Guid workId,
            IWorkDetailReadService workDetailReadService,
            CancellationToken ct) =>
        {
            var detail = await workDetailReadService.GetAsync(workId, ct);
            return detail is null ? Results.NotFound() : Results.Ok(detail.Editions);
        })
        .WithName("GetWorkEditions")
        .WithSummary("Returns editions and owned assets for a single work.")
        .Produces<List<EditionDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapGet("/{workId:guid}/cast", async (
            Guid workId,
            IPersonCreditReadService personCreditReadService,
            CancellationToken ct) =>
        {
            var cast = await personCreditReadService.BuildForWorkAsync(workId, ct);
            return Results.Ok(cast);
        })
        .WithName("GetWorkCast")
        .WithSummary("Returns actor and character credits for a single work.")
        .Produces<List<CastCreditDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        return app;
    }
}
