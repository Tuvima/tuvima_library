using MediaEngine.Api.Security;
using MediaEngine.Api.Services.Details;
using MediaEngine.Contracts.Details;

namespace MediaEngine.Api.Endpoints;

public static class DetailEndpoints
{
    public static IEndpointRouteBuilder MapDetailEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/details")
            .WithTags("Details");

        group.MapGet("/{entityType}/{id:guid}", async (
            string entityType,
            Guid id,
            string? context,
            DetailComposerService composer,
            CancellationToken ct) =>
        {
            if (!DetailComposerService.TryParseEntityType(entityType, out var parsedType))
                return Results.BadRequest(new { message = $"Unsupported detail entity type '{entityType}'." });

            var presentationContext = DetailComposerService.ParseContext(context);
            var detail = await composer.BuildAsync(parsedType, id, presentationContext, ct);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        })
        .WithName("GetDetailPage")
        .WithSummary("Returns the unified Tuvima detail-page model for media and related entities.")
        .Produces<DetailPageViewModel>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        return app;
    }
}
