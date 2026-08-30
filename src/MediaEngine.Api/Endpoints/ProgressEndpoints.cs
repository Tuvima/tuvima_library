using System.Security.Claims;
using MediaEngine.Contracts.Authentication;
using MediaEngine.Api.Http;
using MediaEngine.Api.Security;
using MediaEngine.Application.ReadModels;
using MediaEngine.Application.Services;
using MediaEngine.Contracts.Paging;
using MediaEngine.Contracts.Progress;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;

namespace MediaEngine.Api.Endpoints;

public static class ProgressEndpoints
{
    public static IEndpointRouteBuilder MapProgressEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/progress")
                       .WithTags("Progress")
                       .RequireAuthorization(AuthPolicies.Authenticated);

        group.MapGet("/{assetId:guid}", async (
            Guid assetId,
            ClaimsPrincipal user,
            IUserStateStore stateStore,
            CancellationToken ct) =>
        {
            var uid = ResolveUserId(user);
            var state = await stateStore.GetAsync(uid, assetId, ct);
            return state is null
                ? ApiErrors.NotFound("No progress recorded for this asset.")
                : Results.Ok(MapStateResponse(state));
        })
        .Produces<UserStateResponse>(StatusCodes.Status200OK)
        .RequireClientScope(ClientApiScopes.ProgressRead);

        group.MapPut("/{assetId:guid}", async (
            Guid assetId,
            ProgressUpdateRequest body,
            ClaimsPrincipal user,
            IUserStateStore stateStore,
            IMediaAssetRepository assetRepo,
            CancellationToken ct) =>
        {
            var asset = await assetRepo.FindByIdAsync(assetId, ct);
            if (asset is null)
                return ApiErrors.NotFound($"Asset '{assetId}' not found.");

            var state = new UserState
            {
                UserId = ResolveUserId(user),
                AssetId = assetId,
                ContentHash = asset.ContentHash,
                ProgressPct = Math.Clamp(body.ProgressPct, 0.0, 100.0),
                LastAccessed = DateTimeOffset.UtcNow,
                ExtendedProperties = body.ExtendedProperties ?? [],
            };

            await stateStore.SaveAsync(state, ct);
            return Results.Ok(MapStateResponse(state));
        })
        .Produces<UserStateResponse>(StatusCodes.Status200OK)
        .RequireClientScope(ClientApiScopes.ProgressWrite);

        group.MapGet("/recent", async (
            ClaimsPrincipal user,
            int? limit,
            IUserStateStore stateStore,
            CancellationToken ct) =>
        {
            var uid = ResolveUserId(user);
            var page = PagedRequest.From(null, limit, defaultLimit: 10);
            var items = await stateStore.GetRecentAsync(uid, page.Limit, ct);
            return Results.Ok(items.Select(MapStateResponse));
        })
        .Produces<IEnumerable<UserStateResponse>>(StatusCodes.Status200OK)
        .RequireClientScope(ClientApiScopes.ProgressRead);

        group.MapGet("/journey", async (
            ClaimsPrincipal user,
            string? collectionId,
            int? limit,
            IJourneyReadService journeyReadService,
            CancellationToken ct) =>
        {
            var uid = ResolveUserId(user);
            var parsedCollectionId = Guid.TryParse(collectionId, out var value) ? value : (Guid?)null;
            var page = PagedRequest.From(null, limit, defaultLimit: 5);
            IReadOnlyList<JourneyItemResponse> results =
                await journeyReadService.GetJourneyAsync(uid, parsedCollectionId, page.Limit, ct);
            return Results.Ok(results.Select(MapJourneyItem).ToList());
        })
        .Produces<IReadOnlyList<JourneyItemDto>>(StatusCodes.Status200OK)
        .RequireClientScope(ClientApiScopes.ProgressRead);

        return app;
    }

    private static Guid ResolveUserId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(TuvimaClaimTypes.ActiveProfileId), out var parsed)
            ? parsed
            : throw new UnauthorizedAccessException("The authenticated profile identity is missing.");

    private static UserStateResponse MapStateResponse(UserState s) => new(
        UserId: s.UserId,
        AssetId: s.AssetId,
        ContentHash: s.ContentHash,
        ProgressPct: s.ProgressPct,
        LastAccessed: s.LastAccessed.UtcDateTime,
        ExtendedProperties: s.ExtendedProperties);

    private static JourneyItemDto MapJourneyItem(JourneyItemResponse source) => new(
        source.AssetId,
        source.WorkId,
        source.CollectionId,
        source.Title,
        source.Author,
        source.CoverUrl,
        source.BackgroundUrl,
        source.BannerUrl,
        source.LogoUrl,
        source.CoverWidthPx,
        source.CoverHeightPx,
        source.BackgroundWidthPx,
        source.BackgroundHeightPx,
        source.BannerWidthPx,
        source.BannerHeightPx,
        source.Narrator,
        source.Series,
        source.SeriesPosition,
        source.Description,
        source.MediaType,
        source.ProgressPct,
        source.LastAccessed,
        source.CollectionDisplayName,
        source.ExtendedProperties,
        source.HeroUrl);
}
