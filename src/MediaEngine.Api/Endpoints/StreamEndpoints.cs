using MediaEngine.Api.Security;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Services;
using MediaEngine.Processors.Contracts;

namespace MediaEngine.Api.Endpoints;

public static class StreamEndpoints
{
    private static readonly Dictionary<string, string> MimeMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".mp4"]  = "video/mp4",
            [".mkv"]  = "video/x-matroska",
            [".avi"]  = "video/x-msvideo",
            [".mp3"]  = "audio/mpeg",
            [".m4a"]  = "audio/mp4",
            [".m4b"]  = "audio/mp4",
            [".ogg"]  = "audio/ogg",
            [".epub"] = "application/epub+zip",
            [".cbz"]  = "application/x-cbz",
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

        group.MapGet("/{assetId:guid}/cover", async (
            Guid assetId,
            IMediaAssetRepository assetRepo,
            IWorkRepository workRepo,
            ICanonicalValueRepository canonicalRepo,
            ImagePathService imagePathService,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var asset = await assetRepo.FindByIdAsync(assetId, ct);
            if (asset is null)
                return Results.NotFound($"Asset '{assetId}' not found.");

            // Resolve image path — try .images/ first, fall back to legacy location
            // alongside the media file for backward compatibility with existing libraries.
            //
            // QID lookup: wikidata_qid canonicals are stored on the WORK entity
            // (via the Priority Cascade), not the asset entity. Walk the lineage
            // asset → edition → work to find the correct entity for the QID lookup.
            var lineage = await workRepo.GetLineageByAssetAsync(assetId, ct);
            var qidEntityId = lineage?.WorkId ?? assetId;
            var canonicals = await canonicalRepo.GetByEntityAsync(qidEntityId, ct);
            var wikidataQid = canonicals
                .FirstOrDefault(c => c.Key is "wikidata_qid"
                    && !string.IsNullOrEmpty(c.Value)
                    && !c.Value.StartsWith("NF", StringComparison.OrdinalIgnoreCase))
                ?.Value;

            var newCoverPath  = imagePathService.GetWorkCoverPath(wikidataQid, assetId);
            // Pending fallback: images downloaded before the QID was resolved live in
            // _pending/{assetId12}/ until SweepPendingToQid moves them. Serve from there
            // when the QID-keyed path doesn't exist yet.
            var pendingCoverPath = imagePathService.GetWorkCoverPath(null, assetId);
            var coverDir = string.IsNullOrEmpty(asset.FilePathRoot)
                ? null
                : Path.GetDirectoryName(asset.FilePathRoot);
            var legacyCoverPath = coverDir is null ? null : Path.Combine(coverDir, "cover.jpg");
            // CoverArtWorker writes poster.jpg (via ImagePathService.GetMediaFilePosterPath),
            // not cover.jpg — use the same path logic so shared-folder covers are found.
            if (legacyCoverPath is not null && !File.Exists(legacyCoverPath) && !string.IsNullOrEmpty(asset.FilePathRoot))
            {
                var posterPath = ImagePathService.GetMediaFilePosterPath(asset.FilePathRoot);
                if (File.Exists(posterPath))
                    legacyCoverPath = posterPath;
            }

            string? coverPath = null;
            bool servedFromPending = false;
            if (File.Exists(newCoverPath))
                coverPath = newCoverPath;
            else if (File.Exists(pendingCoverPath))
            {
                coverPath = pendingCoverPath;
                servedFromPending = true;
            }
            else if (!string.IsNullOrEmpty(legacyCoverPath) && File.Exists(legacyCoverPath))
                coverPath = legacyCoverPath;

            // Parent-chain fallback: child works (tracks, episodes) store cover art under
            // the parent work's QID directory (album, show). Walk up the lineage to find it.
            if (coverPath is null)
                coverPath = await TryResolveParentCoverAsync(
                    assetId, workRepo, assetRepo, canonicalRepo,
                    imagePathService, "cover.jpg", ct);

            if (coverPath is null)
                return Results.NotFound("No cover art found for this asset.");

            if (servedFromPending)
            {
                loggerFactory
                    .CreateLogger("MediaEngine.Api.StreamEndpoints")
                    .LogWarning(
                        "Cover art served from _pending for {EntityId} — needs sweep",
                        assetId);
            }

            var bytes = await File.ReadAllBytesAsync(coverPath, ct);
            return Results.File(bytes, "image/jpeg", "cover.jpg");
        })
        .WithName("GetAssetCover")
        .WithSummary("Serve cover.jpg — checks .images/ first, falls back to legacy location alongside media file.")
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
            ICanonicalValueRepository canonicalRepo,
            ImagePathService imagePathService,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var asset = await assetRepo.FindByIdAsync(assetId, ct);
            if (asset is null)
                return Results.NotFound($"Asset '{assetId}' not found.");

            // Resolve Wikidata QID from canonicals — same lineage walk as /cover.
            // wikidata_qid canonicals live on the Work entity, not the asset.
            var lineage = await workRepo.GetLineageByAssetAsync(assetId, ct);
            var qidEntityId = lineage?.WorkId ?? assetId;
            var canonicals = await canonicalRepo.GetByEntityAsync(qidEntityId, ct);
            var wikidataQid = canonicals
                .FirstOrDefault(c => c.Key is "wikidata_qid"
                    && !string.IsNullOrEmpty(c.Value)
                    && !c.Value.StartsWith("NF", StringComparison.OrdinalIgnoreCase))
                ?.Value;

            var thumbPath = imagePathService.GetWorkCoverThumbPath(wikidataQid, assetId);
            bool servedFromPending = false;

            // Fall back to full cover if no thumbnail exists yet.
            if (!File.Exists(thumbPath))
            {
                var coverPath = imagePathService.GetWorkCoverPath(wikidataQid, assetId);
                if (File.Exists(coverPath))
                {
                    thumbPath = coverPath;
                }
                else
                {
                    // Pending fallback: images downloaded before QID resolution live in
                    // _pending/{assetId12}/ until SweepPendingToQid promotes them.
                    var pendingThumbPath = imagePathService.GetWorkCoverThumbPath(null, assetId);
                    var pendingCoverPath = imagePathService.GetWorkCoverPath(null, assetId);
                    if (File.Exists(pendingThumbPath))
                    {
                        thumbPath = pendingThumbPath;
                        servedFromPending = true;
                    }
                    else if (File.Exists(pendingCoverPath))
                    {
                        thumbPath = pendingCoverPath;
                        servedFromPending = true;
                    }
                    else
                    {
                        // Legacy fallback: cover.jpg alongside the media file, then
                        // poster paths (what CoverArtWorker actually writes via
                        // ImagePathService.GetMediaFilePosterPath/GetMediaFileThumbPath)
                        // — use the same path logic so shared-folder covers are found.
                        var legacyDir = string.IsNullOrEmpty(asset.FilePathRoot)
                            ? null
                            : Path.GetDirectoryName(asset.FilePathRoot);
                        var legacyPath = legacyDir is null ? null : Path.Combine(legacyDir, "cover.jpg");
                        if (legacyPath is not null && !File.Exists(legacyPath) && !string.IsNullOrEmpty(asset.FilePathRoot))
                        {
                            var posterThumbPath = ImagePathService.GetMediaFileThumbPath(asset.FilePathRoot);
                            var posterPath      = ImagePathService.GetMediaFilePosterPath(asset.FilePathRoot);
                            if (File.Exists(posterThumbPath))
                                legacyPath = posterThumbPath;
                            else if (File.Exists(posterPath))
                                legacyPath = posterPath;
                        }
                        if (string.IsNullOrEmpty(legacyPath) || !File.Exists(legacyPath))
                        {
                            // Parent-chain fallback: child works (tracks, episodes) store cover
                            // art under the parent work's QID directory (album, show).
                            var parentThumb = await TryResolveParentCoverAsync(
                                assetId, workRepo, assetRepo, canonicalRepo,
                                imagePathService, "cover_thumb.jpg", ct);
                            // If no thumbnail exists under the parent, accept the full cover too.
                            parentThumb ??= await TryResolveParentCoverAsync(
                                assetId, workRepo, assetRepo, canonicalRepo,
                                imagePathService, "cover.jpg", ct);
                            if (parentThumb is null)
                                return Results.NotFound("No cover art found for this asset.");
                            thumbPath = parentThumb;
                        }
                        else
                        {
                            thumbPath = legacyPath;
                        }
                    }
                }
            }

            if (servedFromPending)
            {
                loggerFactory
                    .CreateLogger("MediaEngine.Api.StreamEndpoints")
                    .LogWarning(
                        "Cover thumbnail served from _pending for {EntityId} — needs sweep",
                        assetId);
            }

            var bytes = await File.ReadAllBytesAsync(thumbPath, ct);
            return Results.File(bytes, "image/jpeg", "cover_thumb.jpg");
        })
        .WithName("GetAssetCoverThumb")
        .WithSummary("Serve cover_thumb.jpg (200px wide) — falls back to full cover when thumbnail is not yet generated.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();
        // NOTE: No rate limit — thumbnails are loaded in bulk on Home/category pages.
        // The 100/min streaming cap was causing 429s on page reloads with many swimlanes.

        group.MapGet("/{assetId:guid}/hero", async (
            Guid assetId,
            IMediaAssetRepository assetRepo,
            ICanonicalValueRepository canonicalRepo,
            ImagePathService imagePathService,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var asset = await assetRepo.FindByIdAsync(assetId, ct);
            if (asset is null)
                return Results.NotFound($"Asset '{assetId}' not found.");

            // Resolve image path — try .images/ first, fall back to legacy location.
            var canonicals = await canonicalRepo.GetByEntityAsync(assetId, ct);
            var wikidataQid = canonicals
                .FirstOrDefault(c => c.Key is "wikidata_qid"
                    && !string.IsNullOrEmpty(c.Value)
                    && !c.Value.StartsWith("NF", StringComparison.OrdinalIgnoreCase))
                ?.Value;

            var newHeroPath  = imagePathService.GetWorkHeroPath(wikidataQid, assetId);
            // Pending fallback: hero banners generated before the QID was resolved live
            // in _pending/{assetId12}/ until SweepPendingToQid moves them.
            var pendingHeroPath = imagePathService.GetWorkHeroPath(null, assetId);
            var legacyHeroPath = string.IsNullOrEmpty(asset.FilePathRoot)
                ? null
                : Path.Combine(Path.GetDirectoryName(asset.FilePathRoot) ?? string.Empty, "hero.jpg");

            string? heroPath = null;
            bool servedFromPending = false;
            if (File.Exists(newHeroPath))
                heroPath = newHeroPath;
            else if (File.Exists(pendingHeroPath))
            {
                heroPath = pendingHeroPath;
                servedFromPending = true;
            }
            else if (!string.IsNullOrEmpty(legacyHeroPath) && File.Exists(legacyHeroPath))
                heroPath = legacyHeroPath;

            if (heroPath is null)
                return Results.NotFound("No hero banner found for this asset.");

            if (servedFromPending)
            {
                loggerFactory
                    .CreateLogger("MediaEngine.Api.StreamEndpoints")
                    .LogWarning(
                        "Hero banner served from _pending for {EntityId} — needs sweep",
                        assetId);
            }

            var bytes = await File.ReadAllBytesAsync(heroPath, ct);
            return Results.File(bytes, "image/jpeg", "hero.jpg");
        })
        .WithName("GetAssetHero")
        .WithSummary("Serve hero.jpg — checks .images/ first, falls back to legacy location alongside media file.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole()
        .RequireRateLimiting("streaming");

        return app;
    }

    /// <summary>
    /// Walks up the work lineage for <paramref name="assetId"/> and looks for
    /// <paramref name="targetFile"/> (e.g. "cover.jpg" or "cover_thumb.jpg")
    /// under the PARENT work's QID image directory.
    ///
    /// Child works — music tracks, TV episodes, comic issues — do not receive
    /// their own cover art; it lives under the parent (album, show, series).
    /// This method bridges that gap so the streaming endpoints can serve art
    /// for child assets without requiring per-track image downloads.
    /// </summary>
    /// <returns>
    /// Full filesystem path to the image if found, or <c>null</c> when the
    /// asset has no parent, the parent has no QID, or the file does not exist.
    /// </returns>
    private static async Task<string?> TryResolveParentCoverAsync(
        Guid assetId,
        IWorkRepository workRepo,
        IMediaAssetRepository assetRepo,
        ICanonicalValueRepository canonicalRepo,
        ImagePathService imagePathService,
        string targetFile,
        CancellationToken ct)
    {
        // Step 1: get the lineage for this asset to find the parent work.
        var lineage = await workRepo.GetLineageByAssetAsync(assetId, ct);
        if (lineage is null || lineage.ParentWorkId is null)
            return null;

        // RootParentWorkId is the top-most parent (show for TV, album for music).
        // For most hierarchies this is the same as ParentWorkId, but for
        // TV (Show → Season → Episode) it walks all the way to the Show.
        var parentWorkId = lineage.RootParentWorkId;

        // Step 2: find any asset that belongs to the parent work so we can
        // look up the parent's canonical values (keyed by asset ID).
        var parentAsset = await assetRepo.FindFirstByWorkIdAsync(parentWorkId, ct);
        if (parentAsset is null)
            return null;

        // Step 3: resolve the parent's Wikidata QID from its canonical values.
        var parentCanonicals = await canonicalRepo.GetByEntityAsync(parentAsset.Id, ct);
        var parentQid = parentCanonicals
            .FirstOrDefault(c => c.Key is "wikidata_qid"
                && !string.IsNullOrEmpty(c.Value)
                && !c.Value.StartsWith("NF", StringComparison.OrdinalIgnoreCase))
            ?.Value;

        if (string.IsNullOrEmpty(parentQid))
            return null;

        // Step 4: build the path and return it if the file exists.
        // The parent's images live at .data/images/works/{parentQID}/{parentAssetId12}/
        var candidatePath = Path.Combine(
            imagePathService.GetWorkImageDir(parentQid, parentAsset.Id),
            targetFile);

        return File.Exists(candidatePath) ? candidatePath : null;
    }

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
