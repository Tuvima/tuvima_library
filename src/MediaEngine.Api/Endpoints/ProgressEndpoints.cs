using System.Text.Json.Serialization;
using MediaEngine.Application.ReadModels;
using MediaEngine.Application.Services;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;

namespace MediaEngine.Api.Endpoints;

public static class ProgressEndpoints
{
    public static IEndpointRouteBuilder MapProgressEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/progress")
                       .WithTags("Progress");

        group.MapGet("/{assetId:guid}", async (
            Guid assetId,
            string? userId,
            IUserStateStore stateStore,
            CancellationToken ct) =>
        {
            var uid = ResolveUserId(userId);
            var state = await stateStore.GetAsync(uid, assetId, ct);
            return state is null
                ? Results.NotFound("No progress recorded for this asset.")
                : Results.Ok(MapStateResponse(state));
        });

        group.MapPut("/{assetId:guid}", async (
            Guid assetId,
            ProgressUpdateRequest body,
            IUserStateStore stateStore,
            IMediaAssetRepository assetRepo,
            CancellationToken ct) =>
        {
            var asset = await assetRepo.FindByIdAsync(assetId, ct);
            if (asset is null)
                return Results.NotFound($"Asset '{assetId}' not found.");

            var state = new UserState
            {
                UserId = ResolveUserId(body.UserId),
                AssetId = assetId,
                ContentHash = asset.ContentHash,
                ProgressPct = Math.Clamp(body.ProgressPct, 0.0, 100.0),
                LastAccessed = DateTimeOffset.UtcNow,
                ExtendedProperties = body.ExtendedProperties ?? [],
            };

            await stateStore.SaveAsync(state, ct);
            return Results.Ok(MapStateResponse(state));
        });

        group.MapGet("/recent", async (
            string? userId,
            int? limit,
            IUserStateStore stateStore,
            CancellationToken ct) =>
        {
            var uid = ResolveUserId(userId);
            var items = await stateStore.GetRecentAsync(uid, limit ?? 10, ct);
            return Results.Ok(items.Select(MapStateResponse));
        });

        group.MapGet("/journey", async (
            string? userId,
            string? collectionId,
            int? limit,
            IJourneyReadService journeyReadService,
            CancellationToken ct) =>
        {
            var uid = ResolveUserId(userId);
            var parsedCollectionId = Guid.TryParse(collectionId, out var value) ? value : (Guid?)null;
            IReadOnlyList<JourneyItemResponse> results =
                await journeyReadService.GetJourneyAsync(uid, parsedCollectionId, limit ?? 5, ct);
            return Results.Ok(results);
        });

        return app;
    }

    private static Guid ResolveUserId(string? userId) =>
        Guid.TryParse(userId, out var parsed)
            ? parsed
            : Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static object MapStateResponse(UserState s) => new
    {
        user_id = s.UserId,
        asset_id = s.AssetId,
        content_hash = s.ContentHash,
        progress_pct = s.ProgressPct,
        last_accessed = s.LastAccessed,
        extended_properties = s.ExtendedProperties,
    };
}

public sealed record ProgressUpdateRequest(
    [property: JsonPropertyName("user_id")] string? UserId,
    [property: JsonPropertyName("progress_pct")] double ProgressPct,
    [property: JsonPropertyName("extended_properties")] Dictionary<string, string>? ExtendedProperties);
