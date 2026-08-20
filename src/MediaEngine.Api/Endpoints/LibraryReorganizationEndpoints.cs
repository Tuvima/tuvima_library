using MediaEngine.Api.Http;
using MediaEngine.Api.Security;
using MediaEngine.Api.Services.Libraries;
using MediaEngine.Contracts.Ingestion;

namespace MediaEngine.Api.Endpoints;

public static class LibraryReorganizationEndpoints
{
    public static IEndpointRouteBuilder MapLibraryReorganizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/settings/libraries/{libraryId:guid}/reorganization")
            .WithTags("Settings", "Libraries")
            .RequireAdmin();

        group.MapPost("/plan", (
            Guid libraryId,
            CreateReorganizationPlanRequest request,
            LibraryReorganizationService service,
            CancellationToken ct) =>
        {
            try
            {
                var plan = service.CreatePlan(libraryId, request, ct);
                return plan is null
                    ? ApiErrors.NotFound($"Library '{libraryId}' was not found.")
                    : Results.Ok(plan);
            }
            catch (ArgumentException exception)
            {
                return ApiErrors.BadRequest(exception.Message);
            }
        })
        .WithName("CreateLibraryReorganizationPlan")
        .WithSummary("Create a read-only filesystem reorganization preview and confirmation fingerprint.")
        .Produces<ReorganizationPlanDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/execute", (
            Guid libraryId,
            ExecuteReorganizationPlanRequest request,
            LibraryReorganizationService service,
            CancellationToken ct) =>
        {
            try
            {
                var result = service.Execute(libraryId, request, ct);
                return result is null
                    ? ApiErrors.NotFound($"Reorganization plan '{request.PlanId}' was not found for library '{libraryId}'.")
                    : Results.Ok(result);
            }
            catch (InvalidOperationException exception)
            {
                return ApiErrors.Conflict(exception.Message);
            }
            catch (ArgumentException exception)
            {
                return ApiErrors.BadRequest(exception.Message);
            }
        })
        .WithName("ExecuteLibraryReorganizationPlan")
        .WithSummary("Confirm and execute the exact previewed plan after revalidating every filesystem mutation.")
        .Produces<ReorganizationExecutionDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}
