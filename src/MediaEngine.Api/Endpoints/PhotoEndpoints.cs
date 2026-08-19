using MediaEngine.Api.Http;
using MediaEngine.Api.Security;
using MediaEngine.Api.Services.Photos;
using MediaEngine.Contracts.Paging;
using MediaEngine.Contracts.Photos;
using MediaEngine.Storage;
using SkiaSharp;

namespace MediaEngine.Api.Endpoints;

public static class PhotoEndpoints
{
    public static IEndpointRouteBuilder MapPhotoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/photos").WithTags("Photos");

        group.MapGet("/", (
            PhotoLibraryRepository repository,
            int? offset, int? limit, string? q, bool? favorites, bool? hidden, Guid? album,
            CancellationToken ct) =>
        {
            var page = PagedRequest.From(offset, limit, defaultLimit: 120, maxLimit: 500);
            var result = repository.Query(
                page.Offset, page.Limit, q, favorites == true, hidden == true, album,
                hiddenOnly: hidden == true, ct: ct);
            return Results.Ok(new PhotoPageDto(
                result.Items, page.Offset, page.Limit, result.Total,
                page.Offset + result.Items.Count < result.Total));
        })
        .WithName("GetPhotos")
        .WithSummary("Browse the local photo timeline with search, favorite, hidden, and album filters.")
        .Produces<PhotoPageDto>()
        .RequireAnyRole();

        group.MapPost("/scan", async (PhotoLibraryService service, CancellationToken ct) =>
            Results.Ok(await service.ScanAsync(ct)))
        .WithName("ScanPhotoLibraries")
        .WithSummary("Index every configured photo library in place without external metadata ingestion.")
        .Produces<PhotoScanResultDto>()
        .RequireAdminOrCurator();

        group.MapPut("/{id:guid}/favorite", async (
            Guid id, SetPhotoFlagRequest request, PhotoLibraryRepository repository, CancellationToken ct) =>
            await repository.SetFlagAsync(id, "favorite", request.Value, ct)
                ? Results.NoContent()
                : ApiErrors.NotFound($"Photo '{id}' was not found."))
        .WithName("SetPhotoFavorite")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapPut("/{id:guid}/hidden", async (
            Guid id, SetPhotoFlagRequest request, PhotoLibraryRepository repository, CancellationToken ct) =>
            await repository.SetFlagAsync(id, "hidden", request.Value, ct)
                ? Results.NoContent()
                : ApiErrors.NotFound($"Photo '{id}' was not found."))
        .WithName("SetPhotoHidden")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapGet("/{id:guid}/content", (Guid id, PhotoLibraryRepository repository, CancellationToken ct) =>
        {
            var source = repository.ResolveContent(id, ct);
            if (source is null || !File.Exists(source.Value.FilePath))
            {
                return ApiErrors.NotFound($"Photo '{id}' was not found on disk.");
            }

            return Results.File(source.Value.FilePath, source.Value.MimeType, enableRangeProcessing: true);
        })
        .WithName("GetPhotoContent")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAnyRole()
        .RequireRateLimiting("streaming");

        group.MapGet("/{id:guid}/thumbnail", (Guid id, PhotoLibraryRepository repository, CancellationToken ct) =>
        {
            var source = repository.ResolveContent(id, ct);
            if (source is null || !File.Exists(source.Value.FilePath))
            {
                return ApiErrors.NotFound($"Photo '{id}' was not found on disk.");
            }

            try
            {
                using var input = File.OpenRead(source.Value.FilePath);
                using var bitmap = SKBitmap.Decode(input);
                if (bitmap is null)
                {
                    return Results.File(source.Value.FilePath, source.Value.MimeType);
                }

                const int maxEdge = 640;
                var scale = Math.Min(1d, maxEdge / (double)Math.Max(bitmap.Width, bitmap.Height));
                var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
                var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
                using var resized = bitmap.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear));
                using var image = SKImage.FromBitmap(resized ?? bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Jpeg, 82);
                return Results.Bytes(data.ToArray(), "image/jpeg");
            }
            catch
            {
                return Results.File(source.Value.FilePath, source.Value.MimeType);
            }
        })
        .WithName("GetPhotoThumbnail")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAnyRole()
        .RequireRateLimiting("streaming");

        group.MapGet("/albums", (PhotoLibraryRepository repository, CancellationToken ct) =>
            Results.Ok(repository.GetAlbums(ct)))
        .WithName("GetPhotoAlbums")
        .Produces<IReadOnlyList<PhotoAlbumDto>>()
        .RequireAnyRole();

        group.MapPost("/albums", async (
            CreatePhotoAlbumRequest request, PhotoLibraryRepository repository, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 100)
            {
                return ApiErrors.BadRequest("Album name is required and must be at most 100 characters.");
            }

            return Results.Ok(await repository.CreateAlbumAsync(request.Name, request.Description, ct));
        })
        .WithName("CreatePhotoAlbum")
        .Produces<PhotoAlbumDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAnyRole();

        group.MapPost("/albums/{albumId:guid}/items", async (
            Guid albumId, AddPhotoAlbumItemsRequest request, PhotoLibraryRepository repository, CancellationToken ct) =>
            Results.Ok(new AddPhotoAlbumItemsResult(
                await repository.AddToAlbumAsync(albumId, request.PhotoIds, ct))))
        .WithName("AddPhotoAlbumItems")
        .Produces<AddPhotoAlbumItemsResult>()
        .RequireAnyRole();

        return app;
    }
}
