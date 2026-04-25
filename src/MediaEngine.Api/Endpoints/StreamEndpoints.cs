using MediaEngine.Api.Security;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Services;
using MediaEngine.Processors.Contracts;

namespace MediaEngine.Api.Endpoints;

public static class StreamEndpoints
{
    private static readonly Dictionary<string, string> MimeMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".mp4"]  = "video/mp4",
            [".m4v"]  = "video/x-m4v",
            [".mkv"]  = "video/x-matroska",
            [".webm"] = "video/webm",
            [".avi"]  = "video/x-msvideo",
            [".mp3"]  = "audio/mpeg",
            [".m4a"]  = "audio/mp4",
            [".m4b"]  = "audio/mp4",
            [".aac"]  = "audio/aac",
            [".flac"] = "audio/flac",
            [".ogg"]  = "audio/ogg",
            [".wav"]  = "audio/wav",
            [".epub"] = "application/epub+zip",
            [".cbz"]  = "application/x-cbz",
            [".cbr"]  = "application/x-cbr",
            [".pdf"]  = "application/pdf",
        };

    public static IEndpointRouteBuilder MapStreamEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/stream")
                       .WithTags("Streaming");

        group.MapGet("/{assetId:guid}", async (
            Guid assetId,
            HttpContext ctx,
            IMediaAssetRepository assetRepo,
            IByteStreamer streamer,
            CancellationToken ct) =>
        {
            var asset = await assetRepo.FindByIdAsync(assetId, ct);
            if (asset is null)
                return Results.NotFound($"Asset '{assetId}' not found.");

            if (!File.Exists(asset.FilePathRoot))
                return Results.Problem(
                    detail: $"File not found on disk: {asset.FilePathRoot}",
                    statusCode: StatusCodes.Status500InternalServerError);

            var ext      = Path.GetExtension(asset.FilePathRoot);
            var mimeType = MimeMap.GetValueOrDefault(ext, "application/octet-stream");

            ctx.Response.Headers.AcceptRanges = "bytes";
            long totalSize = await streamer.GetFileSizeAsync(asset.FilePathRoot, ct);

            if (ctx.Request.Headers.TryGetValue("Range", out var rangeHeader)
                && TryParseRange(rangeHeader.ToString(), totalSize,
                                 out long rangeStart, out long rangeEnd))
            {
                long length = rangeEnd - rangeStart + 1;
                using var result = await streamer.GetRangeAsync(
                    asset.FilePathRoot, rangeStart, length, ct);

                ctx.Response.StatusCode             = StatusCodes.Status206PartialContent;
                ctx.Response.ContentType            = mimeType;
                ctx.Response.Headers.ContentRange   = result.ContentRangeHeader;
                ctx.Response.Headers.ContentLength  = result.ContentLength;
                await result.Content.CopyToAsync(ctx.Response.Body, ct);
                return Results.Empty;
            }
            else
            {
                using var result = await streamer.GetRangeAsync(
                    asset.FilePathRoot, 0, null, ct);

                ctx.Response.ContentType           = mimeType;
                ctx.Response.Headers.ContentLength = totalSize;
                await result.Content.CopyToAsync(ctx.Response.Body, ct);
                return Results.Empty;
            }
        })
        .WithName("StreamAsset")
        .WithSummary("Stream a media asset with HTTP 206 byte-range support.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status206PartialContent)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole()
        .RequireRateLimiting("streaming");

        group.MapGet("/artwork/{variantId:guid}", async (
            Guid variantId,
            string? size,
            IEntityAssetRepository entityAssetRepo,
            IHttpClientFactory httpFactory,
            CancellationToken ct) =>
        {
            var variant = await entityAssetRepo.FindByIdAsync(variantId, ct);
            if (variant is null)
                return Results.NotFound($"Artwork variant '{variantId}' not found.");

            var renditionPath = ResolveArtworkPath(variant, size);
            if (!string.IsNullOrWhiteSpace(renditionPath) && File.Exists(renditionPath))
            {
                var bytes = await File.ReadAllBytesAsync(renditionPath, ct);
                return Results.File(bytes, GetImageMimeType(renditionPath), Path.GetFileName(renditionPath));
            }

            if (!string.IsNullOrWhiteSpace(variant.ImageUrl)
                && Uri.TryCreate(variant.ImageUrl, UriKind.Absolute, out var imageUri)
                && (imageUri.Scheme == Uri.UriSchemeHttp || imageUri.Scheme == Uri.UriSchemeHttps))
            {
                using var client = httpFactory.CreateClient("cover_download");
                using var response = await client.GetAsync(imageUri, ct);
                if (!response.IsSuccessStatusCode)
                    return Results.NotFound("Artwork source could not be retrieved.");

                var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
                return Results.File(bytes, contentType);
            }

            if (!string.IsNullOrWhiteSpace(variant.ImageUrl)
                && variant.ImageUrl.StartsWith("/", StringComparison.Ordinal))
            {
                return Results.Redirect(variant.ImageUrl);
            }

            return Results.NotFound("Artwork file not found.");
        })
        .WithName("GetArtworkVariant")
        .WithSummary("Serve artwork by variant id.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status302Found)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapGet("/{assetId:guid}/cover", async (
            Guid assetId,
            IMediaAssetRepository assetRepo,
            IWorkRepository workRepo,
            IEntityAssetRepository entityAssetRepo,
            CancellationToken ct) =>
        {
            var asset = await assetRepo.FindByIdAsync(assetId, ct);
            if (asset is null)
                return Results.NotFound($"Asset '{assetId}' not found.");

            var ownerEntityId = await ResolveArtworkOwnerEntityIdAsync(assetId, workRepo, ct);
            var preferredVariant = await entityAssetRepo.GetPreferredAsync(ownerEntityId.ToString(), "CoverArt", ct);
            if (!string.IsNullOrWhiteSpace(preferredVariant?.LocalImagePath) && File.Exists(preferredVariant.LocalImagePath))
            {
                var variantBytes = await File.ReadAllBytesAsync(preferredVariant.LocalImagePath, ct);
                return Results.File(
                    variantBytes,
                    GetImageMimeType(preferredVariant.LocalImagePath),
                    Path.GetFileName(preferredVariant.LocalImagePath));
            }

            return Results.NotFound("No cover art found for this asset.");
        })
        .WithName("GetAssetCover")
        .WithSummary("Serve the preferred centrally-managed cover artwork for a media asset.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();
        // NOTE: No rate limit — cover art is small, cacheable, and loaded in bulk on
        // Home/category pages (dozens per reload). The streaming policy (100/min) is
        // sized for true media streams, not static thumbnails.

        group.MapGet("/{assetId:guid}/cover-thumb", async (
            Guid assetId,
            IMediaAssetRepository assetRepo,
            IWorkRepository workRepo,
            IEntityAssetRepository entityAssetRepo,
            CancellationToken ct) =>
        {
            var asset = await assetRepo.FindByIdAsync(assetId, ct);
            if (asset is null)
                return Results.NotFound($"Asset '{assetId}' not found.");

            var ownerEntityId = await ResolveArtworkOwnerEntityIdAsync(assetId, workRepo, ct);
            var preferredVariant = await entityAssetRepo.GetPreferredAsync(ownerEntityId.ToString(), "CoverArt", ct);
            var thumbPath = preferredVariant is null ? null : ResolveArtworkPath(preferredVariant, "s");
            if (!string.IsNullOrWhiteSpace(thumbPath) && File.Exists(thumbPath))
            {
                var variantBytes = await File.ReadAllBytesAsync(thumbPath, ct);
                return Results.File(
                    variantBytes,
                    GetImageMimeType(thumbPath),
                    Path.GetFileName(thumbPath));
            }

            return Results.NotFound("No cover art found for this asset.");
        })
        .WithName("GetAssetCoverThumb")
        .WithSummary("Serve the centrally-managed derived cover thumbnail for a media asset.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();
        // NOTE: No rate limit — thumbnails are loaded in bulk on Home/category pages.
        // The 100/min streaming cap was causing 429s on page reloads with many swimlanes.

        group.MapGet("/{assetId:guid}/banner", async (
            Guid assetId,
            IMediaAssetRepository assetRepo,
            IWorkRepository workRepo,
            IEntityAssetRepository entityAssetRepo,
            CancellationToken ct) =>
        {
            var asset = await assetRepo.FindByIdAsync(assetId, ct);
            if (asset is null)
                return Results.NotFound($"Asset '{assetId}' not found.");

            var ownerEntityId = await ResolveArtworkOwnerEntityIdAsync(assetId, workRepo, ct);
            var preferredVariant = await entityAssetRepo.GetPreferredAsync(ownerEntityId.ToString(), "Banner", ct);
            var bannerPath = preferredVariant is null ? null : ResolveArtworkPath(preferredVariant, null);
            if (!string.IsNullOrWhiteSpace(bannerPath) && File.Exists(bannerPath))
            {
                var preferredBytes = await File.ReadAllBytesAsync(bannerPath, ct);
                return Results.File(preferredBytes, GetImageMimeType(bannerPath), Path.GetFileName(bannerPath));
            }

            return Results.NotFound("No banner artwork found for this asset.");
        })
        .WithName("GetAssetBanner")
        .WithSummary("Serve uploaded banner artwork for a media asset.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapGet("/{assetId:guid}/square", async (
            Guid assetId,
            IMediaAssetRepository assetRepo,
            IWorkRepository workRepo,
            IEntityAssetRepository entityAssetRepo,
            CancellationToken ct) =>
        {
            var asset = await assetRepo.FindByIdAsync(assetId, ct);
            if (asset is null)
                return Results.NotFound($"Asset '{assetId}' not found.");

            var ownerEntityId = await ResolveArtworkOwnerEntityIdAsync(assetId, workRepo, ct);
            var preferredVariant = await entityAssetRepo.GetPreferredAsync(ownerEntityId.ToString(), "SquareArt", ct);
            var squarePath = preferredVariant is null ? null : ResolveArtworkPath(preferredVariant, null);
            if (!string.IsNullOrWhiteSpace(squarePath) && File.Exists(squarePath))
            {
                var preferredBytes = await File.ReadAllBytesAsync(squarePath, ct);
                return Results.File(preferredBytes, GetImageMimeType(squarePath), Path.GetFileName(squarePath));
            }

            return Results.NotFound("No square artwork found for this asset.");
        })
        .WithName("GetAssetSquareArt")
        .WithSummary("Serve uploaded square artwork for a media asset.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapGet("/{assetId:guid}/background", async (
            Guid assetId,
            IMediaAssetRepository assetRepo,
            IWorkRepository workRepo,
            IEntityAssetRepository entityAssetRepo,
            CancellationToken ct) =>
        {
            var asset = await assetRepo.FindByIdAsync(assetId, ct);
            if (asset is null)
                return Results.NotFound($"Asset '{assetId}' not found.");

            var ownerEntityId = await ResolveArtworkOwnerEntityIdAsync(assetId, workRepo, ct);
            var preferredVariant = await entityAssetRepo.GetPreferredAsync(ownerEntityId.ToString(), "Background", ct);
            var backgroundPath = preferredVariant is null ? null : ResolveArtworkPath(preferredVariant, null);
            if (!string.IsNullOrWhiteSpace(backgroundPath) && File.Exists(backgroundPath))
            {
                var preferredBytes = await File.ReadAllBytesAsync(backgroundPath, ct);
                return Results.File(preferredBytes, GetImageMimeType(backgroundPath), Path.GetFileName(backgroundPath));
            }

            return Results.NotFound("No background artwork found for this asset.");
        })
        .WithName("GetAssetBackground")
        .WithSummary("Serve uploaded background artwork for a media asset.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapGet("/{assetId:guid}/logo", async (
            Guid assetId,
            IMediaAssetRepository assetRepo,
            IWorkRepository workRepo,
            IEntityAssetRepository entityAssetRepo,
            CancellationToken ct) =>
        {
            var asset = await assetRepo.FindByIdAsync(assetId, ct);
            if (asset is null)
                return Results.NotFound($"Asset '{assetId}' not found.");

            var ownerEntityId = await ResolveArtworkOwnerEntityIdAsync(assetId, workRepo, ct);
            var preferredVariant = await entityAssetRepo.GetPreferredAsync(ownerEntityId.ToString(), "Logo", ct);
            var logoPath = preferredVariant is null ? null : ResolveArtworkPath(preferredVariant, null);
            if (!string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath))
            {
                var preferredBytes = await File.ReadAllBytesAsync(logoPath, ct);
                return Results.File(preferredBytes, GetImageMimeType(logoPath), Path.GetFileName(logoPath));
            }

            return Results.NotFound("No logo artwork found for this asset.");
        })
        .WithName("GetAssetLogo")
        .WithSummary("Serve uploaded logo artwork for a media asset.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        return app;
    }

    private static async Task<Guid> ResolveArtworkOwnerEntityIdAsync(
        Guid assetId,
        IWorkRepository workRepo,
        CancellationToken ct,
        Guid? fallbackOwnerEntityId = null)
    {
        var lineage = await workRepo.GetLineageByAssetAsync(assetId, ct);
        return lineage?.TargetForParentScope ?? fallbackOwnerEntityId ?? assetId;
    }

    private static string? ResolveArtworkPath(EntityAsset asset, string? size) => (size ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "s" => asset.LocalImagePathSmall ?? asset.LocalImagePath,
        "m" => asset.LocalImagePathMedium ?? asset.LocalImagePath,
        "l" => asset.LocalImagePathLarge ?? asset.LocalImagePath,
        _ => asset.LocalImagePath,
    };

    private static string GetImageMimeType(string path) =>
        string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase)
            ? "image/png"
            : "image/jpeg";

    /// <summary>
    /// Parses the RFC 7233 Range header value "bytes=start-end".
    /// Both start and end may be absent. Returns false if the header cannot be
    /// parsed or the range is unsatisfiable.
    /// </summary>
    private static bool TryParseRange(
        string rangeHeader,
        long totalSize,
        out long start,
        out long end)
    {
        start = 0;
        end   = totalSize > 0 ? totalSize - 1 : 0;

        if (!rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            return false;

        var rangePart = rangeHeader["bytes=".Length..];
        var dashIdx   = rangePart.IndexOf('-');
        if (dashIdx < 0)
            return false;

        var startStr = rangePart[..dashIdx].Trim();
        var endStr   = rangePart[(dashIdx + 1)..].Trim();

        // "bytes=-500" → last 500 bytes (suffix range).
        if (startStr.Length == 0 && long.TryParse(endStr, out long suffixLength))
        {
            start = Math.Max(0, totalSize - suffixLength);
            end   = totalSize - 1;
            return totalSize > 0;
        }

        if (!long.TryParse(startStr, out start))
            return false;

        if (endStr.Length == 0)
            end = totalSize - 1;
        else if (!long.TryParse(endStr, out end))
            return false;

        start = Math.Max(0, start);
        end   = Math.Min(end, totalSize - 1);
        return start <= end && totalSize > 0;
    }
}
