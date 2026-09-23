using MediaEngine.Api.Http;
using MediaEngine.Api.Security;
using MediaEngine.Api.Services.View;
using MediaEngine.Contracts.LocalAssets;
using MediaEngine.Contracts.Paging;
using MediaEngine.Domain.PersonalMedia;

namespace MediaEngine.Api.Endpoints;

public static class ViewDiscoveryEndpoints
{
    public static IEndpointRouteBuilder MapViewDiscoveryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/view").WithTags("View")
            .RequireAuthorization(AuthPolicies.Authenticated);

        group.MapGet("/places", async (
            string? scope,
            Guid? scopeProfileId,
            string? q,
            string? cursor,
            int? limit,
            ViewDiscoveryService service,
            CancellationToken ct) =>
        {
            try
            {
                var page = PagedRequest.From(null, limit, defaultLimit: 50, maxLimit: 100);
                var request = new ViewDiscoveryRequest(
                    ParseScope(scope, scopeProfileId),
                    page.Limit,
                    q,
                    cursor);
                return ToResult(await service.GetPlacesAsync(request, ct).ConfigureAwait(false));
            }
            catch (ArgumentException exception)
            {
                return ApiErrors.BadRequest(exception.Message);
            }
        })
        .WithName("GetViewPlaces")
        .WithSummary("List real location groups from the caller's authorized View scope.")
        .Produces<ViewPlacesPageDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/places/atlas", async (
            string? scope,
            Guid? scopeProfileId,
            string? q,
            int? year,
            string? kind,
            ViewDiscoveryService service,
            CancellationToken ct) =>
        {
            try
            {
                return ToResult(await service.GetAtlasAsync(
                    ParseScope(scope, scopeProfileId), q, year, kind, ct).ConfigureAwait(false));
            }
            catch (ArgumentException exception)
            {
                return ApiErrors.BadRequest(exception.Message);
            }
        })
        .WithName("GetViewAtlas")
        .WithSummary("Returns authorization-scoped world hotspots and Atlas time facets.")
        .Produces<ViewAtlasPageDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/places/media", async (
            string placeKey,
            string? scope,
            Guid? scopeProfileId,
            int? offset,
            int? limit,
            int? year,
            string? kind,
            ViewDiscoveryService service,
            CancellationToken ct) =>
        {
            try
            {
                var page = PagedRequest.From(offset, limit, defaultLimit: 250, maxLimit: 500);
                return ToResult(await service.GetPlaceMediaAsync(
                    ParseScope(scope, scopeProfileId), placeKey, page.Offset, page.Limit,
                    year, kind, ct).ConfigureAwait(false));
            }
            catch (ArgumentException exception)
            {
                return ApiErrors.BadRequest(exception.Message);
            }
        })
        .WithName("GetViewPlaceMedia")
        .WithSummary("Returns date-ordered media for one authorized Atlas hotspot.")
        .Produces<ViewPlaceMediaPageDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/people", async (
            string? scope,
            Guid? scopeProfileId,
            string? q,
            string? cursor,
            int? limit,
            ViewDiscoveryService service,
            CancellationToken ct) =>
        {
            try
            {
                var page = PagedRequest.From(null, limit, defaultLimit: 100, maxLimit: 100);
                var request = new ViewDiscoveryRequest(
                    ParseScope(scope, scopeProfileId),
                    page.Limit,
                    q,
                    cursor);
                return ToResult(await service.GetPeopleAsync(request, ct).ConfigureAwait(false));
            }
            catch (ArgumentException exception)
            {
                return ApiErrors.BadRequest(exception.Message);
            }
        })
        .WithName("GetViewPeople")
        .WithSummary("List provenance-aware named or reviewed people from the caller's authorized View scope.")
        .Produces<ViewPeoplePageDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    internal static ViewScopeRequest ParseScope(string? value, Guid? profileId)
    {
        var kind = string.IsNullOrWhiteSpace(value) ? ViewScopeKind.Shared : value.Trim().ToLowerInvariant() switch
        {
            "shared" => ViewScopeKind.Shared,
            "mine" => ViewScopeKind.Mine,
            "profile" => ViewScopeKind.Profile,
            _ => throw new ArgumentException("View scope must be shared, mine, or profile.", nameof(value)),
        };
        if (kind == ViewScopeKind.Profile && profileId is null)
        {
            throw new ArgumentException("Profile scope requires scopeProfileId.", nameof(profileId));
        }

        if (kind != ViewScopeKind.Profile && profileId is not null)
        {
            throw new ArgumentException("scopeProfileId is valid only for profile scope.", nameof(profileId));
        }

        return kind == ViewScopeKind.Profile
            ? ViewScopeRequest.ForProfile(profileId!.Value)
            : kind == ViewScopeKind.Mine ? ViewScopeRequest.Mine : ViewScopeRequest.Shared;
    }

    private static IResult ToResult(ViewPlacesResult result) => result.Outcome switch
    {
        ViewAccessOutcome.Allowed when result.Page is not null => Results.Ok(result.Page),
        ViewAccessOutcome.Unauthenticated => Results.Unauthorized(),
        ViewAccessOutcome.Forbidden => ApiErrors.Forbidden("View access is not permitted."),
        _ => ApiErrors.NotFound("The requested View scope was not found."),
    };

    private static IResult ToResult(ViewPeopleResult result) => result.Outcome switch
    {
        ViewAccessOutcome.Allowed when result.Page is not null => Results.Ok(result.Page),
        ViewAccessOutcome.Unauthenticated => Results.Unauthorized(),
        ViewAccessOutcome.Forbidden => ApiErrors.Forbidden("View access is not permitted."),
        _ => ApiErrors.NotFound("The requested View scope was not found."),
    };

    private static IResult ToResult(ViewAtlasResult result) => result.Outcome switch
    {
        ViewAccessOutcome.Allowed when result.Page is not null => Results.Ok(result.Page),
        ViewAccessOutcome.Unauthenticated => Results.Unauthorized(),
        ViewAccessOutcome.Forbidden => ApiErrors.Forbidden("View access is not permitted."),
        _ => ApiErrors.NotFound("The requested View scope was not found."),
    };

    private static IResult ToResult(ViewPlaceMediaResult result) => result.Outcome switch
    {
        ViewAccessOutcome.Allowed when result.Page is not null => Results.Ok(result.Page),
        ViewAccessOutcome.Unauthenticated => Results.Unauthorized(),
        ViewAccessOutcome.Forbidden => ApiErrors.Forbidden("View access is not permitted."),
        _ => ApiErrors.NotFound("The requested View scope was not found."),
    };
}
