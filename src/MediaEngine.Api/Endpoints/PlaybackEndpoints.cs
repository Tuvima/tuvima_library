using MediaEngine.Api.Security;
using MediaEngine.Api.Services.Playback;
using MediaEngine.Contracts.Playback;

namespace MediaEngine.Api.Endpoints;

public static class PlaybackEndpoints
{
    private static readonly Dictionary<string, string> MimeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = "video/mp4",
        [".m4v"] = "video/x-m4v",
        [".m3u8"] = "application/vnd.apple.mpegurl",
        [".ts"] = "video/mp2t",
        [".mp3"] = "audio/mpeg",
        [".m4a"] = "audio/mp4",
        [".m4b"] = "audio/mp4",
        [".aac"] = "audio/aac",
        [".flac"] = "audio/flac",
        [".ogg"] = "audio/ogg",
        [".wav"] = "audio/wav",
    };

    public static IEndpointRouteBuilder MapPlaybackEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/playback")
            .WithTags("Playback");

        group.MapGet("/{assetId:guid}/manifest", async (
            Guid assetId,
            string? client,
            PlaybackCapabilitiesService playback,
            CancellationToken ct) =>
        {
            var manifest = await playback.BuildManifestAsync(assetId, client, ct);
            return manifest is null
                ? Results.NotFound($"Asset '{assetId}' not found.")
                : Results.Ok(manifest);
        })
        .WithName("GetPlaybackManifest")
        .WithSummary("Return the centralized playback manifest for an asset and client profile.")
        .Produces<PlaybackManifestDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapPost("/{assetId:guid}/encode", async (
            Guid assetId,
            QueueEncodeRequestDto request,
            PlaybackCapabilitiesService playback,
            CancellationToken ct) =>
        {
            var job = await playback.QueueEncodeAsync(assetId, request, ct);
            return job is null
                ? Results.NotFound($"Asset '{assetId}' not found.")
                : Results.Accepted($"/playback/encode/jobs/{job.Id}", job);
        })
        .WithName("QueueEncodeJob")
        .WithSummary("Queue or schedule a managed encode/offline variant job.")
        .Produces<EncodeJobDto>(StatusCodes.Status202Accepted)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapGet("/encode/jobs", async (
            PlaybackCapabilitiesService playback,
            CancellationToken ct) =>
        {
            var jobs = await playback.ListEncodeJobsAsync(ct);
            return Results.Ok(jobs);
        })
        .WithName("ListEncodeJobs")
        .WithSummary("List recent encode jobs.")
        .RequireAnyRole();

        group.MapPost("/encode/jobs/{jobId:guid}/cancel", async (
            Guid jobId,
            PlaybackCapabilitiesService playback,
            CancellationToken ct) =>
        {
            await playback.CancelEncodeJobAsync(jobId, ct);
            return Results.NoContent();
        })
        .WithName("CancelEncodeJob")
        .WithSummary("Cancel a queued, scheduled, or running encode job.")
        .RequireAnyRole();

        group.MapGet("/diagnostics", async (
            PlaybackCapabilitiesService playback,
            CancellationToken ct) =>
        {
            var diagnostics = await playback.GetDiagnosticsAsync(ct);
            return Results.Ok(diagnostics);
        })
        .WithName("GetPlaybackDiagnostics")
        .WithSummary("Report playback, inspection, and encode readiness.")
        .RequireAnyRole();

        group.MapGet("/{assetId:guid}/offline/{variantId:guid}", async (
            Guid assetId,
            Guid variantId,
            PlaybackCapabilitiesService playback,
            CancellationToken ct) =>
        {
            var variant = await playback.GetOfflineVariantFileAsync(assetId, variantId, ct);
            if (variant is null)
            {
                return Results.NotFound("Offline variant not found.");
            }

            if (!string.Equals(variant.Status, OfflineVariantStatuses.Ready, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(variant.OutputPath)
                || !File.Exists(variant.OutputPath))
            {
                return Results.NotFound("Offline variant is not ready.");
            }

            var stream = File.OpenRead(variant.OutputPath);
            var mime = MimeMap.GetValueOrDefault(Path.GetExtension(variant.OutputPath), "application/octet-stream");
            return Results.File(stream, mime, Path.GetFileName(variant.OutputPath), enableRangeProcessing: true);
        })
        .WithName("DownloadOfflineVariant")
        .WithSummary("Download a prepared offline variant with HTTP range support.")
        .RequireAnyRole()
        .RequireRateLimiting("streaming");

        return app;
    }
}
