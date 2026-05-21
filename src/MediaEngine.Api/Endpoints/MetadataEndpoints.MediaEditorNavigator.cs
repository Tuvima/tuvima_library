using MediaEngine.Api.Security;
using MediaEngine.Api.Services.ReadServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace MediaEngine.Api.Endpoints;

public static partial class MetadataEndpoints
{
    private static void MapMediaEditorNavigatorEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/{entityId:guid}/navigator", async (
            Guid entityId,
            IMediaEditorNavigationReadService navigationReadService,
            CancellationToken ct) =>
        {
            var navigator = await navigationReadService.GetNavigatorAsync(entityId, ct);
            return navigator is null
                ? Results.NotFound($"Navigator for {entityId} not found.")
                : Results.Ok(navigator);
        })
        .WithName("GetMediaEditorNavigator")
        .WithSummary("Resolve series-aware editor navigation for a launch entity.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapGet("/{entityId:guid}/membership-suggestions", async (
            Guid entityId,
            string field,
            string? query,
            string? source,
            Guid? parentEntityId,
            string? parentValue,
            IMediaEditorMembershipReadService membershipReadService,
            CancellationToken ct) =>
        {
            var suggestions = await membershipReadService.GetSuggestionsAsync(entityId, field, query, source, parentEntityId, parentValue, ct);
            return Results.Ok(suggestions);
        })
        .WithName("GetMediaEditorMembershipSuggestions")
        .WithSummary("Return same-media-type autocomplete targets for membership correction.")
        .Produces(StatusCodes.Status200OK)
        .RequireAnyRole();

        group.MapPost("/{entityId:guid}/membership-preview", async (
            Guid entityId,
            MediaEditorNavigationReadService.MembershipPreviewRequest request,
            IMediaEditorMembershipReadService membershipReadService,
            CancellationToken ct) =>
        {
            var preview = await membershipReadService.PreviewAsync(entityId, request, ct);
            return preview is null
                ? Results.NotFound($"Membership preview for {entityId} not found.")
                : Results.Ok(preview);
        })
        .WithName("PreviewMediaEditorMembershipChange")
        .WithSummary("Preview a hierarchy move or parent identity rename before applying it.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapPost("/{entityId:guid}/membership-apply", async (
            Guid entityId,
            MediaEditorNavigationReadService.MembershipPreviewRequest request,
            IMediaEditorMembershipReadService membershipReadService,
            CancellationToken ct) =>
        {
            var result = await membershipReadService.ApplyAsync(entityId, request, ct);
            return result is null
                ? Results.NotFound($"Membership apply for {entityId} not found.")
                : Results.Ok(result);
        })
        .WithName("ApplyMediaEditorMembershipChange")
        .WithSummary("Apply a confirmed hierarchy move or parent identity rename.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();
    }
}
