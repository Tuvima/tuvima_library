using MediaEngine.Api.Security;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Services;
using MediaEngine.Processors.Contracts;
using MediaEngine.Providers.Helpers;
using SkiaSharp;

namespace MediaEngine.Api.Endpoints;

public static class StreamEndpoints
{
    private const string ArtworkPlaceholderSvg = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 320 480" role="img" aria-label="Artwork unavailable">
          <defs>
            <linearGradient id="bg" x1="0" y1="0" x2="1" y2="1">
              <stop offset="0" stop-color="#1f2937"/>
              <stop offset="0.55" stop-color="#334155"/>
              <stop offset="1" stop-color="#0f172a"/>
            </linearGradient>
          </defs>
          <rect width="320" height="480" rx="16" fill="url(#bg)"/>
          <rect x="68" y="146" width="184" height="188" rx="12" fill="none" stroke="#94a3b8" stroke-width="10" opacity="0.72"/>
          <circle cx="124" cy="204" r="24" fill="#cbd5e1" opacity="0.78"/>
          <path d="M82 308l56-62 38 42 24-28 38 48z" fill="#cbd5e1" opacity="0.78"/>
        </svg>
        """;

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
            AssetPathService assetPathService,
            IHttpClientFactory httpFactory,
            CancellationToken ct) =>
        {
            var variant = await entityAssetRepo.FindByIdAsync(variantId, ct);
            if (variant is null)
                return Results.NotFound($"Artwork variant '{variantId}' not found.");

            var hasRequestedSize = !string.IsNullOrWhiteSpace(size);
            var normalizedSize = NormalizeArtworkSize(size);
            if (hasRequestedSize && normalizedSize is null)
            {
                return Results.BadRequest("Artwork size must be one of 's', 'm', or 'l'.");
            }

            if (normalizedSize is not null)
            {
                await EnsureArtworkRenditionsAsync(variant, entityAssetRepo, assetPathService, ct);
            }

            var renditionPath = ResolveArtworkPath(variant, normalizedSize);
            var localArtworkResult = CreateLocalArtworkResult(renditionPath);
            if (localArtworkResult is not null)
            {
                return localArtworkResult;
            }

            if (normalizedSize is not null)
            {
                return CreateArtworkPlaceholderResult();
            }

            if (!string.IsNullOrWhiteSpace(variant.ImageUrl)
                && Uri.TryCreate(variant.ImageUrl, UriKind.Absolute, out var imageUri)
                && (imageUri.Scheme == Uri.UriSchemeHttp || imageUri.Scheme == Uri.UriSchemeHttps))
            {
                using var client = httpFactory.CreateClient("cover_download");
                using var response = await client.GetAsync(imageUri, ct);
                if (!response.IsSuccessStatusCode)
                    return CreateArtworkPlaceholderResult();

                var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
                return Results.File(bytes, contentType);
            }

            if (!string.IsNullOrWhiteSpace(variant.ImageUrl)
                && variant.ImageUrl.StartsWith("/", StringComparison.Ordinal))
            {
                return Results.Redirect(variant.ImageUrl);
            }

            return CreateArtworkPlaceholderResult();
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
            var localArtworkResult = CreateLocalArtworkResult(preferredVariant?.LocalImagePath);
            if (localArtworkResult is not null)
            {
                return localArtworkResult;
            }

            return CreateArtworkPlaceholderResult();
        })
        .WithName("GetAssetCover")
        .WithSummary("Serve the preferred centrally-managed cover artwork for a media asset.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();
        // NOTE: No rate limit — cover art is small, cacheable, and loaded in bulk on
        // Home/category pages (dozens per reload). The streaming policy (100/min) is
        // sized for true media streams, not static thumbnails.

        group.MapGet("/{assetId:guid}/text-tracks", async (
            Guid assetId,
            IMediaAssetRepository assetRepo,
            ITextTrackRepository textTrackRepo,
            CancellationToken ct) =>
        {
            var asset = await assetRepo.FindByIdAsync(assetId, ct);
            if (asset is null)
                return Results.NotFound($"Asset '{assetId}' not found.");

            var tracks = await textTrackRepo.GetByAssetAsync(assetId, null, ct);
            return Results.Ok(tracks.Select(t => new TextTrackDto(
                t.Id,
                t.Kind.ToString(),
                t.Language,
                t.Provider,
                t.Confidence,
                t.SourceFormat,
                t.NormalizedFormat,
                t.TimingMode,
                t.IsHearingImpaired,
                t.IsPreferred,
                t.IsUserOwned,
                t.SidecarPath is not null,
                t.Kind == TextTrackKind.Lyrics
                    ? $"/stream/{assetId}/lyrics"
                    : $"/stream/{assetId}/subtitles?language={Uri.EscapeDataString(t.Language)}")));
        })
        .WithName("GetAssetTextTracks")
        .WithSummary("List lyrics and subtitle tracks available for a media asset.")
        .Produces<IReadOnlyList<TextTrackDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapGet("/{assetId:guid}/lyrics", async (
            Guid assetId,
            ITextTrackRepository textTrackRepo,
            CancellationToken ct) =>
        {
            var track = await textTrackRepo.GetPreferredAsync(assetId, TextTrackKind.Lyrics, null, ct);
            if (track is null || string.IsNullOrWhiteSpace(track.LocalPath) || !File.Exists(track.LocalPath))
                return Results.NotFound("No synced lyrics found for this asset.");

            var bytes = await File.ReadAllBytesAsync(track.LocalPath, ct);
            return Results.File(bytes, "text/plain; charset=utf-8", Path.GetFileName(track.LocalPath));
        })
        .WithName("GetAssetLyrics")
        .WithSummary("Serve the preferred synchronized lyrics for a media asset.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapGet("/{assetId:guid}/subtitles", async (
            Guid assetId,
            string? language,
            ITextTrackRepository textTrackRepo,
            CancellationToken ct) =>
        {
            var track = await textTrackRepo.GetPreferredAsync(assetId, TextTrackKind.Subtitles, language, ct);
            if (track is null || string.IsNullOrWhiteSpace(track.LocalPath) || !File.Exists(track.LocalPath))
                return Results.NotFound("No subtitles found for this asset.");

            var bytes = await File.ReadAllBytesAsync(track.LocalPath, ct);
            return Results.File(bytes, "text/vtt; charset=utf-8", Path.GetFileName(track.LocalPath));
        })
        .WithName("GetAssetSubtitles")
        .WithSummary("Serve the preferred normalized WebVTT subtitles for a media asset.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapPost("/{assetId:guid}/text-tracks/refresh", async (
            Guid assetId,
            string? kind,
            IMediaAssetRepository assetRepo,
            IEnrichmentService enrichmentService,
            CancellationToken ct) =>
        {
            var asset = await assetRepo.FindByIdAsync(assetId, ct);
            if (asset is null)
                return Results.NotFound($"Asset '{assetId}' not found.");

            var type = string.Equals(kind, "subtitles", StringComparison.OrdinalIgnoreCase)
                ? EnrichmentType.Subtitles
                : EnrichmentType.TimedLyrics;
            await enrichmentService.RunSingleEnrichmentAsync(assetId, string.Empty, type, ct);
            return Results.Ok(new
            {
                asset_id = assetId,
                enrichment_type = type.ToString(),
                refreshed = true,
            });
        })
        .WithName("RefreshAssetTextTracks")
        .WithSummary("Manually refresh timed lyrics or subtitles for a media asset.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

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
            var localArtworkResult = CreateLocalArtworkResult(thumbPath);
            if (localArtworkResult is not null)
            {
                return localArtworkResult;
            }

            return CreateArtworkPlaceholderResult();
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
            var localArtworkResult = CreateLocalArtworkResult(bannerPath);
            if (localArtworkResult is not null)
            {
                return localArtworkResult;
            }

            return CreateArtworkPlaceholderResult();
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
            var localArtworkResult = CreateLocalArtworkResult(squarePath);
            if (localArtworkResult is not null)
            {
                return localArtworkResult;
            }

            return CreateArtworkPlaceholderResult();
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
            var localArtworkResult = CreateLocalArtworkResult(backgroundPath);
            if (localArtworkResult is not null)
            {
                return localArtworkResult;
            }

            return CreateArtworkPlaceholderResult();
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
            var localArtworkResult = CreateLocalArtworkResult(logoPath);
            if (localArtworkResult is not null)
            {
                return localArtworkResult;
            }

            return CreateArtworkPlaceholderResult();
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
        if (lineage is null)
            return fallbackOwnerEntityId ?? assetId;

        return lineage.MediaType switch
        {
            MediaEngine.Domain.Enums.MediaType.Books
                or MediaEngine.Domain.Enums.MediaType.Audiobooks
                or MediaEngine.Domain.Enums.MediaType.Comics => lineage.TargetForSelfScope,
            _ => lineage.TargetForParentScope,
        };
    }

    private static async Task EnsureArtworkRenditionsAsync(
        EntityAsset asset,
        IEntityAssetRepository entityAssetRepo,
        AssetPathService assetPathService,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(asset.LocalImagePath)
            || !File.Exists(asset.LocalImagePath)
            || !ArtworkVariantHelper.ShouldGenerateRenditions(asset.AssetTypeValue))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(asset.LocalImagePathSmall)
            && File.Exists(asset.LocalImagePathSmall)
            && !string.IsNullOrWhiteSpace(asset.LocalImagePathMedium)
            && File.Exists(asset.LocalImagePathMedium)
            && !string.IsNullOrWhiteSpace(asset.LocalImagePathLarge)
            && File.Exists(asset.LocalImagePathLarge))
        {
            return;
        }

        ArtworkVariantHelper.StampMetadataAndRenditions(asset, assetPathService);
        await entityAssetRepo.UpsertAsync(asset, ct);
    }

    private static string? NormalizeArtworkSize(string? size)
    {
        var normalized = (size ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "s" or "m" or "l" ? normalized : null;
    }

    private static string? ResolveArtworkPath(EntityAsset asset, string? size)
    {
        var requestedPath = (size ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "s" => asset.LocalImagePathSmall,
            "m" => asset.LocalImagePathMedium,
            "l" => asset.LocalImagePathLarge,
            _ => asset.LocalImagePath,
        };

        return !string.IsNullOrWhiteSpace(requestedPath) && File.Exists(requestedPath)
            ? requestedPath
            : asset.LocalImagePath;
    }

    private static IResult? CreateLocalArtworkResult(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        var normalizedImage = TryNormalizeArtworkBytes(path);
        if (normalizedImage is null)
        {
            return CreateArtworkPlaceholderResult();
        }

        return Results.File(
            normalizedImage.Value.Bytes,
            normalizedImage.Value.ContentType,
            Path.GetFileName(path));
    }

    private static ArtworkFile? TryNormalizeArtworkBytes(string path)
    {
        try
        {
            using var bitmap = SKBitmap.Decode(path);
            if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0)
            {
                return null;
            }

            var isPng = string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(isPng ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg, isPng ? 100 : 88);
            if (data is null || data.Size <= 0)
            {
                return null;
            }

            return new ArtworkFile(
                data.ToArray(),
                isPng ? "image/png" : "image/jpeg");
        }
        catch
        {
            return null;
        }
    }

    private static IResult CreateArtworkPlaceholderResult() =>
        Results.Text(ArtworkPlaceholderSvg, "image/svg+xml");

    private static string GetImageMimeType(string path) =>
        string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase)
            ? "image/png"
            : "image/jpeg";

    private readonly record struct ArtworkFile(byte[] Bytes, string ContentType);

    private sealed record TextTrackDto(
        Guid Id,
        string Kind,
        string Language,
        string Provider,
        double Confidence,
        string SourceFormat,
        string NormalizedFormat,
        string TimingMode,
        bool IsHearingImpaired,
        bool IsPreferred,
        bool IsUserOwned,
        bool IsLocallyExported,
        string Url);

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
