using MediaEngine.Api.Security;
using MediaEngine.Api.Services.Plugins;
using MediaEngine.Contracts.Playback;
using MediaEngine.Domain.Contracts;

namespace MediaEngine.Api.Endpoints;

internal static class PlaybackSegmentEndpoints
{
    internal static RouteGroupBuilder MapPlaybackSegmentEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/playback")
            .WithTags("Playback Segments");

        group.MapGet("/{assetId:guid}/segments", async (
            Guid assetId,
            IPlaybackSegmentRepository segments,
            CancellationToken ct) =>
        {
            var rows = await segments.ListByAssetAsync(assetId, ct);
            return Results.Ok(rows.Select(PluginSegmentDetectionService.ToDto));
        })
        .WithName("GetPlaybackSegments")
        .Produces<IReadOnlyList<PlaybackSegmentDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        group.MapPost("/{assetId:guid}/segments/detect", async (
            Guid assetId,
            string? pluginId,
            PluginSegmentDetectionService detector,
            CancellationToken ct) =>
        {
            var rows = await detector.DetectAsync(assetId, pluginId, ct);
            return Results.Ok(rows);
        })
        .WithName("DetectPlaybackSegments")
        .RequireAdmin();

        group.MapPut("/{assetId:guid}/segments/{segmentId:guid}", async (
            Guid assetId,
            Guid segmentId,
            UpdatePlaybackSegmentRequest request,
            IPlaybackSegmentRepository segments,
            CancellationToken ct) =>
        {
            var existing = await segments.FindByIdAsync(segmentId, ct);
            if (existing is null || existing.AssetId != assetId)
                return Results.NotFound($"Segment '{segmentId}' not found.");

            existing.Kind = request.Kind ?? existing.Kind;
            existing.StartSeconds = request.StartSeconds ?? existing.StartSeconds;
            existing.EndSeconds = request.EndSeconds ?? existing.EndSeconds;
            existing.Confidence = request.Confidence ?? existing.Confidence;
            existing.IsSkippable = request.IsSkippable ?? existing.IsSkippable;
            existing.ReviewStatus = request.ReviewStatus ?? existing.ReviewStatus;
            existing.Source = request.Source ?? existing.Source;
            await segments.UpdateAsync(existing, ct);
            return Results.Ok(PluginSegmentDetectionService.ToDto(existing));
        })
        .WithName("UpdatePlaybackSegment")
        .RequireAdmin();

        group.MapDelete("/{assetId:guid}/segments/{segmentId:guid}", async (
            Guid assetId,
            Guid segmentId,
            IPlaybackSegmentRepository segments,
            CancellationToken ct) =>
        {
            var existing = await segments.FindByIdAsync(segmentId, ct);
            if (existing is null || existing.AssetId != assetId)
                return Results.NotFound($"Segment '{segmentId}' not found.");

            existing.ReviewStatus = "hidden";
            await segments.UpdateAsync(existing, ct);
            return Results.NoContent();
        })
        .WithName("HidePlaybackSegment")
        .RequireAdmin();

        return group;
    }
}

public sealed record UpdatePlaybackSegmentRequest(
    string? Kind,
    double? StartSeconds,
    double? EndSeconds,
    double? Confidence,
    string? Source,
    bool? IsSkippable,
    string? ReviewStatus);
