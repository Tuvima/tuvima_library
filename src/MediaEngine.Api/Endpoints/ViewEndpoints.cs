using MediaEngine.Api.Http;
using MediaEngine.Api.Security;
using MediaEngine.Api.Services.LocalAssets;
using MediaEngine.Contracts.LocalAssets;
using MediaEngine.Contracts.Paging;
using MediaEngine.Domain.Services;
using MediaEngine.Storage.Contracts;
using SkiaSharp;

namespace MediaEngine.Api.Endpoints;

public static class ViewEndpoints
{
    public static IEndpointRouteBuilder MapViewEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/view").WithTags("View");

        group.MapGet("/libraries", (
            ViewLibraryService service,
            Guid? profileId,
            HttpContext httpContext,
            CancellationToken ct) => Results.Ok(service.GetLibraries(profileId, GetRole(httpContext), ct)))
        .WithName("GetViewLibraries")
        .WithSummary("List configured personal View libraries and their local item counts.")
        .Produces<IReadOnlyList<ViewLibrarySummaryDto>>()
        .RequireAnyRole();

        group.MapGet("/{libraryId:guid}", (
            Guid libraryId,
            ViewLibraryService service,
            ILocalAssetRepository repository,
            int? offset,
            int? limit,
            string? q,
            string[]? kind,
            bool? favorite,
            bool? hidden,
            Guid? collection,
            Guid? profileId,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            if (!service.IsPersonalViewLibrary(libraryId))
            {
                return ApiErrors.NotFound($"View library '{libraryId}' was not found.");
            }
            if (!service.CanAccess(
                    libraryId,
                    profileId,
                    GetRole(httpContext),
                    LibraryAccessAction.Read))
            {
                return ApiErrors.Forbidden($"The selected profile cannot read View library '{libraryId}'.");
            }

            var page = PagedRequest.From(offset, limit, defaultLimit: 120, maxLimit: 500);
            try
            {
                return Results.Ok(repository.Query(new LocalAssetQuery(
                    libraryId,
                    page.Offset,
                    page.Limit,
                    q,
                    kind,
                    FavoritesOnly: favorite == true,
                    IncludeHidden: hidden == true,
                    HiddenOnly: hidden == true,
                    GalleryId: collection), ct));
            }
            catch (ArgumentException exception)
            {
                return ApiErrors.BadRequest(exception.Message);
            }
        })
        .WithName("GetViewLibraryItems")
        .WithSummary("Browse and search one personal View library.")
        .Produces<LocalAssetPageDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapPost("/{libraryId:guid}/scan", async (
            Guid libraryId,
            ViewLibraryService service,
            Guid? profileId,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            if (!service.IsPersonalViewLibrary(libraryId))
            {
                return ApiErrors.NotFound($"View library '{libraryId}' was not found.");
            }
            if (!service.CanAccess(
                    libraryId,
                    profileId,
                    GetRole(httpContext),
                    LibraryAccessAction.Manage))
            {
                return ApiErrors.Forbidden($"The selected profile cannot manage View library '{libraryId}'.");
            }

            var result = await service.ScanAsync(libraryId, ct);
            return result is null
                ? ApiErrors.NotFound($"View library '{libraryId}' was not found.")
                : Results.Ok(result);
        })
        .WithName("ScanViewLibrary")
        .WithSummary("Index a personal View library in place using local metadata only.")
        .Produces<LocalAssetScanResultDto>()
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapPut("/{libraryId:guid}/items/{id:guid}/favorite", async (
            Guid libraryId,
            Guid id,
            SetLocalAssetFlagRequest request,
            ViewLibraryService service,
            ILocalAssetRepository repository,
            Guid? profileId,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            if (!service.IsPersonalViewLibrary(libraryId))
            {
                return ApiErrors.NotFound($"View library '{libraryId}' was not found.");
            }
            if (!service.CanAccess(
                    libraryId,
                    profileId,
                    GetRole(httpContext),
                    LibraryAccessAction.Contribute))
            {
                return ApiErrors.Forbidden($"The selected profile cannot contribute to View library '{libraryId}'.");
            }
            if (!BelongsToLibrary(repository, libraryId, id, ct))
            {
                return ApiErrors.NotFound($"View item '{id}' was not found in library '{libraryId}'.");
            }

            return await repository.SetFlagsAsync(id, request.Value, hidden: null, ct)
                ? Results.NoContent()
                : ApiErrors.NotFound($"View item '{id}' was not found.");
        })
        .WithName("SetViewItemFavorite")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapPut("/{libraryId:guid}/items/{id:guid}/hidden", async (
            Guid libraryId,
            Guid id,
            SetLocalAssetFlagRequest request,
            ViewLibraryService service,
            ILocalAssetRepository repository,
            Guid? profileId,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            if (!service.IsPersonalViewLibrary(libraryId))
            {
                return ApiErrors.NotFound($"View library '{libraryId}' was not found.");
            }
            if (!service.CanAccess(
                    libraryId,
                    profileId,
                    GetRole(httpContext),
                    LibraryAccessAction.Contribute))
            {
                return ApiErrors.Forbidden($"The selected profile cannot contribute to View library '{libraryId}'.");
            }
            if (!BelongsToLibrary(repository, libraryId, id, ct))
            {
                return ApiErrors.NotFound($"View item '{id}' was not found in library '{libraryId}'.");
            }

            return await repository.SetFlagsAsync(id, favorite: null, request.Value, ct)
                ? Results.NoContent()
                : ApiErrors.NotFound($"View item '{id}' was not found.");
        })
        .WithName("SetViewItemHidden")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapGet("/{libraryId:guid}/items/{id:guid}/content", (
            Guid libraryId,
            Guid id,
            string? role,
            ViewLibraryService service,
            ILocalAssetRepository repository,
            Guid? profileId,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            if (!service.IsPersonalViewLibrary(libraryId))
            {
                return ApiErrors.NotFound($"View library '{libraryId}' was not found.");
            }
            if (!service.CanAccess(
                    libraryId,
                    profileId,
                    GetRole(httpContext),
                    LibraryAccessAction.Read))
            {
                return ApiErrors.Forbidden($"The selected profile cannot read View library '{libraryId}'.");
            }

            LocalAssetContentLocation? source;
            try
            {
                source = repository.ResolveContent(
                    id,
                    string.IsNullOrWhiteSpace(role) ? LocalAssetFileRoles.Primary : role,
                    ct);
            }
            catch (ArgumentException exception)
            {
                return ApiErrors.BadRequest(exception.Message);
            }

            if (source is null || source.LibraryId != libraryId || !File.Exists(source.FilePath))
            {
                return ApiErrors.NotFound($"View item '{id}' was not found on disk.");
            }

            return Results.File(source.FilePath, source.MimeType, enableRangeProcessing: true);
        })
        .WithName("GetViewItemContent")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAnyRole()
        .RequireRateLimiting("streaming");

        group.MapGet("/{libraryId:guid}/items/{id:guid}/thumbnail", (
            Guid libraryId,
            Guid id,
            ViewLibraryService service,
            ILocalAssetRepository repository,
            Guid? profileId,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            if (!service.IsPersonalViewLibrary(libraryId))
            {
                return ApiErrors.NotFound($"View library '{libraryId}' was not found.");
            }
            if (!service.CanAccess(
                    libraryId,
                    profileId,
                    GetRole(httpContext),
                    LibraryAccessAction.Read))
            {
                return ApiErrors.Forbidden($"The selected profile cannot read View library '{libraryId}'.");
            }

            var source = repository.ResolveContent(id, LocalAssetFileRoles.Primary, ct);
            if (source is null || source.LibraryId != libraryId || !File.Exists(source.FilePath))
            {
                return ApiErrors.NotFound($"View item '{id}' was not found on disk.");
            }
            if (!source.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return Results.NoContent();
            }

            try
            {
                using var input = File.OpenRead(source.FilePath);
                using var bitmap = SKBitmap.Decode(input);
                if (bitmap is null)
                {
                    return Results.File(source.FilePath, source.MimeType);
                }

                const int maxEdge = 640;
                var scale = Math.Min(1d, maxEdge / (double)Math.Max(bitmap.Width, bitmap.Height));
                var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
                var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
                using var resized = bitmap.Resize(
                    new SKImageInfo(width, height),
                    new SKSamplingOptions(SKFilterMode.Linear));
                using var image = SKImage.FromBitmap(resized ?? bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Jpeg, 82);
                return Results.Bytes(data.ToArray(), "image/jpeg");
            }
            catch
            {
                return Results.File(source.FilePath, source.MimeType);
            }
        })
        .WithName("GetViewItemThumbnail")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAnyRole()
        .RequireRateLimiting("streaming");

        return app;
    }

    private static bool BelongsToLibrary(
        ILocalAssetRepository repository,
        Guid libraryId,
        Guid itemId,
        CancellationToken ct)
    {
        var item = repository.Find(itemId, ct);
        return item?.LibraryId == libraryId;
    }

    private static string? GetRole(HttpContext context) =>
        context.Items.TryGetValue("ApiKeyRole", out var value) ? value as string : null;
}
