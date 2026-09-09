using System.Security.Claims;
using MediaEngine.Api.Http;
using MediaEngine.Api.Security;
using MediaEngine.Api.Services.ReadServices;
using MediaEngine.Contracts.Authentication;
using MediaEngine.Contracts.Collections;
using MediaEngine.Contracts.Persons;
using MediaEngine.Domain.Authorization;

namespace MediaEngine.Api.Endpoints;

public static class WorkEndpoints
{
    public static IEndpointRouteBuilder MapWorkEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/works")
                       .WithTags("Works");

        group.MapGet("/{workId:guid}", async (
            Guid workId,
            HttpContext context,
            IWorkDetailReadService workDetailReadService,
            CatalogueResourceAuthorizationService authorization,
            CancellationToken ct) =>
        {
            var detail = await workDetailReadService.GetAsync(workId, ct);
            if (detail is null || !await RestrictToAuthorizedAssetsAsync(detail, context, authorization, ct))
            {
                return ApiErrors.NotFound($"Work '{workId}' not found.");
            }

            return Results.Ok(detail);
        })
        .WithName("GetWorkDetail")
        .WithSummary("Returns a single work with canonical values, editions, and owned assets.")
        .Produces<WorkDetailDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireClientScope(ClientApiScopes.LibraryRead);

        group.MapGet("/{workId:guid}/editions", async (
            Guid workId,
            HttpContext context,
            IWorkDetailReadService workDetailReadService,
            CatalogueResourceAuthorizationService authorization,
            CancellationToken ct) =>
        {
            var detail = await workDetailReadService.GetAsync(workId, ct);
            if (detail is null || !await RestrictToAuthorizedAssetsAsync(detail, context, authorization, ct))
            {
                return ApiErrors.NotFound($"Work '{workId}' not found.");
            }

            return Results.Ok(detail.Editions);
        })
        .WithName("GetWorkEditions")
        .WithSummary("Returns editions and owned assets for a single work.")
        .Produces<List<EditionDto>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireClientScope(ClientApiScopes.LibraryRead);

        group.MapGet("/{workId:guid}/cast", async (
            Guid workId,
            HttpContext context,
            IPersonCreditReadService personCreditReadService,
            CatalogueResourceAuthorizationService authorization,
            CancellationToken ct) =>
        {
            if ((await authorization.GetAuthorizedAssetIdsForWorkAsync(
                    context,
                    workId,
                    ActiveProfileId(context.User),
                    ApplicationPermissionIds.LibraryRead,
                    ct)).Count == 0)
            {
                return ApiErrors.NotFound($"Work '{workId}' not found.");
            }

            var cast = await personCreditReadService.BuildForWorkAsync(workId, ct);
            return Results.Ok(cast);
        })
        .WithName("GetWorkCast")
        .WithSummary("Returns actor and character credits for a single work.")
        .Produces<List<CastCreditDto>>(StatusCodes.Status200OK)
        .RequireClientScope(ClientApiScopes.LibraryRead);

        return app;
    }

    private static async Task<bool> RestrictToAuthorizedAssetsAsync(
        WorkDetailDto detail,
        HttpContext context,
        CatalogueResourceAuthorizationService authorization,
        CancellationToken ct)
    {
        var allowed = (await authorization.GetAuthorizedAssetIdsForWorkAsync(
                context,
                detail.Id,
                ActiveProfileId(context.User),
                ApplicationPermissionIds.LibraryRead,
                ct))
            .ToHashSet();
        if (allowed.Count == 0)
        {
            return false;
        }

        foreach (var edition in detail.Editions)
        {
            edition.Assets.RemoveAll(asset => !allowed.Contains(asset.Id));
        }

        detail.Editions.RemoveAll(edition => edition.Assets.Count == 0);
        return detail.Editions.Count > 0;
    }

    private static Guid? ActiveProfileId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(TuvimaClaimTypes.ActiveProfileId), out var profileId)
            ? profileId
            : null;
}
