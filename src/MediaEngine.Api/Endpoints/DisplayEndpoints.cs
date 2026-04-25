using MediaEngine.Contracts.Display;
using MediaEngine.Api.Security;
using MediaEngine.Api.Services.Display;

namespace MediaEngine.Api.Endpoints;

public static class DisplayEndpoints
{
    public static IEndpointRouteBuilder MapDisplayEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/display")
            .WithTags("Display");

        group.MapGet("/home", async (bool? includeCatalog, DisplayComposerService display, CancellationToken ct) =>
            Results.Ok(await display.BuildHomeAsync(includeCatalog ?? true, ct)))
            .WithName("GetDisplayHome")
            .WithSummary("Returns the cross-platform consumer Home display model.")
            .Produces<DisplayPageDto>(StatusCodes.Status200OK)
            .RequireAnyRole();

        group.MapGet("/browse", async (
            string? lane,
            string? mediaType,
            string? grouping,
            string? search,
            int? offset,
            int? limit,
            bool? includeCatalog,
            Guid? profileId,
            DisplayComposerService display,
            CancellationToken ct) =>
            Results.Ok(await display.BuildBrowseAsync(lane, mediaType, grouping, search, offset ?? 0, limit ?? 48, includeCatalog ?? true, profileId, ct)))
            .WithName("GetDisplayBrowse")
            .WithSummary("Returns cross-platform display cards for a media lane or browse query.")
            .Produces<DisplayPageDto>(StatusCodes.Status200OK)
            .RequireAnyRole();

        group.MapGet("/continue", async (
            string? lane,
            int? limit,
            bool? includeCatalog,
            DisplayComposerService display,
            CancellationToken ct) =>
            Results.Ok(await display.BuildContinueAsync(lane, limit ?? 24, includeCatalog ?? true, ct)))
            .WithName("GetDisplayContinue")
            .WithSummary("Returns cross-platform continue cards with progress.")
            .Produces<DisplayPageDto>(StatusCodes.Status200OK)
            .RequireAnyRole();

        group.MapGet("/search", async (
            string? q,
            int? limit,
            bool? includeCatalog,
            DisplayComposerService display,
            CancellationToken ct) =>
            Results.Ok(await display.BuildSearchAsync(q, limit ?? 48, includeCatalog ?? true, ct)))
            .WithName("GetDisplaySearch")
            .WithSummary("Returns consumer search results as display cards.")
            .Produces<DisplayPageDto>(StatusCodes.Status200OK)
            .RequireAnyRole();

        group.MapGet("/shelves/{shelfKey}", async (
            string shelfKey,
            string? lane,
            string? mediaType,
            string? grouping,
            string? search,
            string? cursor,
            int? offset,
            int? limit,
            Guid? profileId,
            DisplayComposerService display,
            CancellationToken ct) =>
        {
            var page = await display.BuildShelfPageAsync(
                shelfKey,
                lane,
                mediaType,
                grouping,
                search,
                cursor,
                offset,
                limit ?? 24,
                profileId,
                ct);
            return page is null ? Results.NotFound() : Results.Ok(page);
        })
            .WithName("GetDisplayShelf")
            .WithSummary("Returns one paged display shelf for native and TV clients.")
            .Produces<DisplayShelfPageDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAnyRole();

        group.MapGet("/groups/{groupId:guid}", async (
            Guid groupId,
            bool? includeCatalog,
            DisplayComposerService display,
            CancellationToken ct) =>
        {
            var page = await display.BuildGroupAsync(groupId, includeCatalog ?? true, ct);
            return page is null ? Results.NotFound() : Results.Ok(page);
        })
            .WithName("GetDisplayGroup")
            .WithSummary("Returns display cards for a consumer group or collection.")
            .Produces<DisplayPageDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAnyRole();

        return app;
    }
}
