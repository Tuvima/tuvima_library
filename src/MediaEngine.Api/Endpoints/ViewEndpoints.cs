using MediaEngine.Api.Http;
using MediaEngine.Api.Security;
using MediaEngine.Api.Services.LocalAssets;
using MediaEngine.Api.Services.View;
using MediaEngine.Contracts.LocalAssets;
using MediaEngine.Contracts.Paging;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.PersonalMedia;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Endpoints;

public static class ViewEndpoints
{
    public static IEndpointRouteBuilder MapViewEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/view").WithTags("View").RequireAnyRole();

        group.MapGet("/scopes", async (string? scope, Guid? scopeProfileId,
            IViewRequestProfileContext identity, IViewProfileRepository preferences,
            IViewScopeResolver resolver, CancellationToken ct) =>
        {
            if (identity.Current is not { } caller) return Unauthenticated();
            var requested = await GetScopeAsync(caller.ProfileId, scope, scopeProfileId, preferences, ct);
            var result = await resolver.ResolveAsync(caller, requested, ct);
            return result is null ? Missing() : Results.Ok(ToContract(result));
        }).WithName("GetViewScopes").Produces<ViewScopeResolutionDto>();

        group.MapGet("/preferences", async (IViewRequestProfileContext identity,
            IViewProfileRepository repository, CancellationToken ct) =>
            identity.Current is not { } caller
                ? Unauthenticated()
                : Results.Ok(ToContract(await repository.GetPreferencesAsync(caller.ProfileId, ct))))
            .WithName("GetViewPreferences").Produces<ViewPreferencesDto>();

        group.MapPut("/preferences", async (ViewPreferencesRequest request,
            IViewRequestProfileContext identity, IViewProfileRepository repository,
            IViewScopeResolver resolver, CancellationToken ct) =>
        {
            if (identity.Current is not { } caller) return Unauthenticated();
            try
            {
                var resolution = await resolver.ResolveAsync(caller,
                    ParseScope(request.Scope, request.ScopeProfileId), ct);
                if (resolution is null) return Missing();
                var value = new ViewProfilePreferences(caller.ProfileId,
                    resolution.Scope.Kind, resolution.Scope.ProfileId,
                    request.TimelineDensity, DateTimeOffset.UtcNow);
                await repository.SavePreferencesAsync(value, ct);
                return Results.Ok(ToContract(value));
            }
            catch (ArgumentException exception) { return ApiErrors.BadRequest(exception.Message); }
        }).WithName("UpdateViewPreferences").Produces<ViewPreferencesDto>();

        group.MapGet("/assets", async (string? scope, Guid? scopeProfileId,
            int? limit, string? cursor, string? q, string[]? kind,
            bool? favorite, bool? hidden, Guid? galleryId, string? lifecycle,
            IViewRequestProfileContext identity, IViewProfileRepository preferences,
            IViewQueryOrchestrator queries, CancellationToken ct) =>
        {
            if (identity.Current is not { } caller) return Unauthenticated();
            try
            {
                var requested = await GetScopeAsync(caller.ProfileId, scope, scopeProfileId, preferences, ct);
                var result = await queries.QueryAsync(new ViewAssetQueryRequest(
                    requested, PagedRequest.From(0, limit, 120, 500).Limit, cursor, q, kind,
                    favorite == true, hidden == true, hidden == true, galleryId,
                    ParseLifecycle(lifecycle)), ct);
                return Access(result.Outcome, result.Page);
            }
            catch (ArgumentException exception) { return ApiErrors.BadRequest(exception.Message); }
            catch (InvalidOperationException exception) { return ApiErrors.Unprocessable(exception.Message); }
        }).WithName("GetViewAssets").Produces<ViewAssetTimelinePageDto>();

        group.MapPost("/uploads", async (IFormFile file,
            IViewRequestProfileContext identity, IViewScopeResolver resolver,
            ViewLibraryService service, CancellationToken ct) =>
        {
            if (identity.Current is not { } caller) return Unauthenticated();
            if (await resolver.ResolveAsync(caller, ViewScopeRequest.Mine, ct) is null) return Missing();
            if (file.Length <= 0) return ApiErrors.BadRequest("No file was uploaded.");
            try
            {
                await using var input = file.OpenReadStream();
                var result = await service.UploadAsync(caller.ProfileId, file.FileName, input, ct);
                return Results.Ok(new ViewUploadResponseDto(
                    result.ItemId, result.ItemAdded, result.FilesAdded, result.SourcesAdded));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException or InvalidOperationException)
            {
                return ApiErrors.BadRequest(exception.Message);
            }
        }).WithName("UploadViewAsset").Produces<ViewUploadResponseDto>().DisableAntiforgery();

        group.MapGet("/items/{id:guid}", async (Guid id, string? scope, Guid? scopeProfileId,
            IViewRequestProfileContext identity, IViewProfileRepository preferences,
            IViewResourceAuthorizationService authorization, ILocalAssetRepository assets,
            CancellationToken ct) =>
        {
            var decision = await AuthorizeItemAsync(id, ViewResourceKind.Asset, ViewResourceAction.Read,
                scope, scopeProfileId, identity, preferences, authorization, ct);
            return decision.IsAllowed && assets.Find(id, ct) is { } item
                ? Results.Ok(item) : Access(decision.Outcome);
        }).WithName("GetViewItem").Produces<LocalAssetDto>();

        group.MapGet("/items/{id:guid}/content", async (Guid id, string? role, string? scope, Guid? scopeProfileId,
            IViewRequestProfileContext identity, IViewProfileRepository preferences,
            IViewResourceAuthorizationService authorization, ILocalAssetRepository assets,
            CancellationToken ct) =>
        {
            var decision = await AuthorizeItemAsync(id, ViewResourceKind.Original, ViewResourceAction.Read,
                scope, scopeProfileId, identity, preferences, authorization, ct);
            if (!decision.IsAllowed) return Access(decision.Outcome);
            try
            {
                var file = assets.ResolveContent(id,
                    string.IsNullOrWhiteSpace(role) ? LocalAssetFileRoles.Primary : role, ct);
                return file is null || !File.Exists(file.FilePath)
                    ? Missing() : Results.File(file.FilePath, file.MimeType, enableRangeProcessing: true);
            }
            catch (ArgumentException exception) { return ApiErrors.BadRequest(exception.Message); }
        }).WithName("GetViewItemContent").Produces(StatusCodes.Status200OK).RequireRateLimiting("streaming");

        group.MapGet("/items/{id:guid}/thumbnail", async (Guid id, string? scope, Guid? scopeProfileId,
            IViewRequestProfileContext identity, IViewProfileRepository preferences,
            IViewResourceAuthorizationService authorization, ILocalAssetRepository assets,
            ViewThumbnailService thumbnails,
            CancellationToken ct) =>
        {
            var decision = await AuthorizeItemAsync(id, ViewResourceKind.Thumbnail, ViewResourceAction.Read,
                scope, scopeProfileId, identity, preferences, authorization, ct);
            if (!decision.IsAllowed) return Access(decision.Outcome);
            var file = assets.ResolveContent(id, LocalAssetFileRoles.Primary, ct);
            if (file is null || !File.Exists(file.FilePath)) return Missing();
            var thumbnail = await thumbnails.GetOrCreateAsync(id, file, ct);
            return thumbnail is null ? Results.NoContent() : Results.File(thumbnail, "image/jpeg");
        }).WithName("GetViewItemThumbnail").Produces(StatusCodes.Status200OK).RequireRateLimiting("streaming");

        MapFlag(group, "favorite", (repo, id, value, ct) => repo.SetFlagsAsync(id, value, null, ct));
        MapFlag(group, "hidden", (repo, id, value, ct) => repo.SetFlagsAsync(id, null, value, ct));
        MapLifecycle(group, "archive", LocalAssetLifecycleState.Archived);
        MapLifecycle(group, "trash", LocalAssetLifecycleState.Trashed);
        MapLifecycle(group, "restore", LocalAssetLifecycleState.Active);
        MapGalleries(group);

        group.MapPost("/admin/libraries/{libraryId:guid}/scan", async (
            Guid libraryId, ViewLibraryService service, CancellationToken ct) =>
            await service.ScanAsync(libraryId, ct) is { } result ? Results.Ok(result) : Missing())
            .WithName("ScanViewLibrary").Produces<LocalAssetScanResultDto>().RequireAdmin();

        return app;
    }

    private static void MapGalleries(RouteGroupBuilder group)
    {
        group.MapGet("/galleries", async (IViewRequestProfileContext identity,
            IViewGalleryRepository repository, CancellationToken ct) =>
        {
            if (identity.Current is not { } caller) return Unauthenticated();
            return Results.Ok(new ViewGalleryListResponse(
                (await repository.GetOwnedAsync(caller.ProfileId, ct)).Select(ToContract).ToList(),
                (await repository.GetSharedWithAsync(caller.ProfileId, ct)).Select(ToContract).ToList()));
        }).WithName("GetViewGalleries").Produces<ViewGalleryListResponse>();

        group.MapPost("/galleries", async (ViewGalleryRequest request,
            IViewRequestProfileContext identity, IViewPersonalSpaceRepository spaces,
            IViewGalleryRepository repository, CancellationToken ct) =>
        {
            if (identity.Current is not { } caller) return Unauthenticated();
            var space = await spaces.GetByOwnerAsync(caller.ProfileId, ct);
            if (space is null) return Missing();
            try
            {
                var gallery = await repository.CreateAsync(new CreateViewGalleryCommand(
                    caller.ProfileId, space.Id, request.Name, request.Kind,
                    request.Description, request.SmartRuleJson, request.CoverItemId, request.SortOrder), ct);
                return Results.Created($"/view/galleries/{gallery.Id:D}", ToContract(gallery));
            }
            catch (ArgumentException exception) { return ApiErrors.BadRequest(exception.Message); }
        }).WithName("CreateViewGallery").Produces<ViewGalleryDto>(StatusCodes.Status201Created);

        group.MapGet("/galleries/{id:guid}", async (Guid id,
            IViewRequestProfileContext identity, IViewResourceAuthorizationService authorization,
            IViewGalleryRepository repository, CancellationToken ct) =>
        {
            var decision = await GalleryAccessAsync(id, ViewResourceAction.Read, identity, authorization, ct);
            return decision.IsAllowed && await repository.GetAsync(id, ct) is { } gallery
                ? Results.Ok(ToContract(gallery)) : Access(decision.Outcome);
        }).WithName("GetViewGallery").Produces<ViewGalleryDto>();

        group.MapPut("/galleries/{id:guid}", async (Guid id, ViewGalleryRequest request,
            IViewRequestProfileContext identity, IViewResourceAuthorizationService authorization,
            IViewGalleryRepository repository, CancellationToken ct) =>
        {
            var decision = await GalleryAccessAsync(id, ViewResourceAction.Manage, identity, authorization, ct);
            if (!decision.IsAllowed) return Access(decision.Outcome);
            try
            {
                var gallery = await repository.UpdateAsync(new UpdateViewGalleryCommand(
                    id, request.Name, request.Description, request.Kind,
                    request.SmartRuleJson, request.CoverItemId, request.SortOrder), ct);
                return gallery is null ? Missing() : Results.Ok(ToContract(gallery));
            }
            catch (ArgumentException exception) { return ApiErrors.BadRequest(exception.Message); }
        }).WithName("UpdateViewGallery").Produces<ViewGalleryDto>();

        group.MapDelete("/galleries/{id:guid}", async (Guid id,
            IViewRequestProfileContext identity, IViewResourceAuthorizationService authorization,
            IViewGalleryRepository repository, CancellationToken ct) =>
        {
            var decision = await GalleryAccessAsync(id, ViewResourceAction.Manage, identity, authorization, ct);
            return !decision.IsAllowed ? Access(decision.Outcome)
                : await repository.DeleteAsync(id, ct) ? Results.NoContent() : Missing();
        }).WithName("DeleteViewGallery")
            .Produces<Microsoft.AspNetCore.Http.HttpResults.NoContent>(StatusCodes.Status204NoContent);

        group.MapGet("/galleries/{id:guid}/items", async (Guid id,
            int? afterPosition, Guid? afterItemId, int? limit,
            IViewRequestProfileContext identity, IViewResourceAuthorizationService authorization,
            IViewGalleryRepository repository, CancellationToken ct) =>
        {
            var decision = await GalleryAccessAsync(id, ViewResourceAction.Read, identity, authorization, ct);
            return !decision.IsAllowed ? Access(decision.Outcome)
                : Results.Ok(ToContract(await repository.GetItemsAsync(id, afterPosition, afterItemId,
                    PagedRequest.From(0, limit, 100, 500).Limit, ct)));
        }).WithName("GetViewGalleryItems").Produces<ViewGalleryItemPageDto>();

        group.MapPost("/galleries/{id:guid}/items", async (Guid id, ViewGalleryItemsRequest request,
            IViewRequestProfileContext identity, IViewResourceAuthorizationService authorization,
            IViewGalleryRepository repository, CancellationToken ct) =>
        {
            var decision = await GalleryAccessAsync(id, ViewResourceAction.Contribute, identity, authorization, ct);
            return !decision.IsAllowed ? Access(decision.Outcome)
                : Results.Ok(ToContract(await repository.AddItemsAsync(id, request.ItemIds, ct)));
        }).WithName("AddViewGalleryItems").Produces<AddViewGalleryItemsResponseDto>();

        group.MapDelete("/galleries/{id:guid}/items", async (Guid id, ViewGalleryItemsRequest request,
            IViewRequestProfileContext identity, IViewResourceAuthorizationService authorization,
            IViewGalleryRepository repository, CancellationToken ct) =>
        {
            var decision = await GalleryAccessAsync(id, ViewResourceAction.Contribute, identity, authorization, ct);
            return !decision.IsAllowed ? Access(decision.Outcome)
                : Results.Ok(new ViewItemsRemovedResponse(
                    await repository.RemoveItemsAsync(id, request.ItemIds, ct)));
        }).WithName("RemoveViewGalleryItems").Produces<ViewItemsRemovedResponse>();

        group.MapPut("/galleries/{id:guid}/items/{itemId:guid}/position", async (
            Guid id, Guid itemId, ViewGalleryPositionRequest request,
            IViewRequestProfileContext identity, IViewResourceAuthorizationService authorization,
            IViewGalleryRepository repository, CancellationToken ct) =>
        {
            var decision = await GalleryAccessAsync(id, ViewResourceAction.Contribute, identity, authorization, ct);
            return !decision.IsAllowed ? Access(decision.Outcome)
                : await repository.SetItemPositionAsync(id, itemId, request.Position, ct)
                    ? Results.NoContent() : Missing();
        }).WithName("ReorderViewGalleryItem")
            .Produces<Microsoft.AspNetCore.Http.HttpResults.NoContent>(StatusCodes.Status204NoContent);

        group.MapGet("/galleries/{id:guid}/shares", async (Guid id,
            IViewRequestProfileContext identity, IViewResourceAuthorizationService authorization,
            IViewGalleryRepository repository, CancellationToken ct) =>
        {
            var decision = await GalleryAccessAsync(id, ViewResourceAction.Manage, identity, authorization, ct);
            return !decision.IsAllowed ? Access(decision.Outcome)
                : Results.Ok((await repository.GetSharesAsync(id, ct)).Select(ToContract).ToList());
        }).WithName("GetViewGalleryShares").Produces<IReadOnlyList<ViewGalleryShareDto>>();

        group.MapPut("/galleries/{id:guid}/shares", async (Guid id, ViewGallerySharesRequest request,
            IViewRequestProfileContext identity, IViewResourceAuthorizationService authorization,
            IViewProfileRepository profiles, IViewGalleryRepository repository, CancellationToken ct) =>
        {
            if (identity.Current is not { } caller) return Unauthenticated();
            var decision = await GalleryAccessAsync(id, ViewResourceAction.Manage, identity, authorization, ct);
            if (!decision.IsAllowed) return Access(decision.Outcome);
            if (!(await profiles.GetPolicyAsync(caller.ProfileId, ct)).ShareGalleries) return Missing();
            await repository.ReplaceSharesAsync(id,
                request.Shares.Select(value => (value.ProfileId, value.Permission)).ToList(), ct);
            return Results.NoContent();
        }).WithName("UpdateViewGalleryShares")
            .Produces<Microsoft.AspNetCore.Http.HttpResults.NoContent>(StatusCodes.Status204NoContent);
    }

    private static void MapFlag(RouteGroupBuilder group, string name,
        Func<ILocalAssetRepository, Guid, bool, CancellationToken, Task<bool>> operation) =>
        group.MapPut($"/items/{{id:guid}}/{name}", async (Guid id, SetLocalAssetFlagRequest request,
            IViewRequestProfileContext identity,
            IViewResourceAuthorizationService authorization, ILocalAssetRepository assets, CancellationToken ct) =>
        {
            var decision = await AuthorizeOwnedItemAsync(id, identity, authorization, ct);
            return !decision.IsAllowed ? Access(decision.Outcome)
                : await operation(assets, id, request.Value, ct) ? Results.NoContent() : Missing();
        }).WithName($"SetViewItem{char.ToUpperInvariant(name[0])}{name[1..]}")
            .Produces<Microsoft.AspNetCore.Http.HttpResults.NoContent>(StatusCodes.Status204NoContent);

    private static void MapLifecycle(RouteGroupBuilder group, string name, LocalAssetLifecycleState state) =>
        group.MapPost($"/items/{{id:guid}}/{name}", async (Guid id,
            IViewRequestProfileContext identity,
            IViewResourceAuthorizationService authorization, ILocalAssetRepository assets, CancellationToken ct) =>
        {
            var decision = await AuthorizeOwnedItemAsync(id, identity, authorization, ct);
            return !decision.IsAllowed ? Access(decision.Outcome)
                : await assets.SetLifecycleStateAsync(id, state, ct) ? Results.NoContent() : Missing();
        }).WithName($"{char.ToUpperInvariant(name[0])}{name[1..]}ViewItem")
            .Produces<Microsoft.AspNetCore.Http.HttpResults.NoContent>(StatusCodes.Status204NoContent);

    private static async Task<ViewAccessDecision> AuthorizeItemAsync(Guid id,
        ViewResourceKind kind, ViewResourceAction action, string? scope, Guid? scopeProfileId,
        IViewRequestProfileContext identity, IViewProfileRepository preferences,
        IViewResourceAuthorizationService authorization, CancellationToken ct)
    {
        if (identity.Current is not { } caller) return ViewAccessDecision.Unauthenticated();
        var selected = await GetScopeAsync(caller.ProfileId, scope, scopeProfileId, preferences, ct);
        return await authorization.AuthorizeAsync(caller, new ViewResourceRequest(selected, kind, id, action), ct);
    }

    private static Task<ViewAccessDecision> GalleryAccessAsync(Guid id, ViewResourceAction action,
        IViewRequestProfileContext identity, IViewResourceAuthorizationService authorization, CancellationToken ct) =>
        authorization.AuthorizeAsync(identity.Current,
            new ViewResourceRequest(ViewScopeRequest.Mine, ViewResourceKind.Gallery, id, action), ct);

    private static Task<ViewAccessDecision> AuthorizeOwnedItemAsync(Guid id,
        IViewRequestProfileContext identity, IViewResourceAuthorizationService authorization, CancellationToken ct) =>
        authorization.AuthorizeAsync(identity.Current,
            new ViewResourceRequest(ViewScopeRequest.Mine, ViewResourceKind.Asset, id,
                ViewResourceAction.Contribute), ct);

    private static async Task<ViewScopeRequest> GetScopeAsync(Guid profileId, string? scope,
        Guid? scopeProfileId, IViewProfileRepository repository, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(scope)) return ParseScope(scope, scopeProfileId);
        var saved = await repository.GetPreferencesAsync(profileId, ct);
        return saved.LastScopeKind.HasValue
            ? new ViewScopeRequest(saved.LastScopeKind.Value, saved.LastScopeProfileId)
            : ViewScopeRequest.Shared;
    }

    private static ViewScopeRequest ParseScope(string? value, Guid? profileId) =>
        value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "mine" => ViewScopeRequest.Mine,
            "shared" => ViewScopeRequest.Shared,
            "profile" when profileId.HasValue => ViewScopeRequest.ForProfile(profileId.Value),
            _ => throw new ArgumentException("Scope must be mine, shared, or profile with scopeProfileId."),
        };

    private static LocalAssetLifecycleFilter ParseLifecycle(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "active" => LocalAssetLifecycleFilter.Active,
            "archived" => LocalAssetLifecycleFilter.Archived,
            "trashed" => LocalAssetLifecycleFilter.Trashed,
            "all" => LocalAssetLifecycleFilter.All,
            _ => throw new ArgumentException("Lifecycle must be active, archived, trashed, or all."),
        };

    private static IResult Access(ViewAccessOutcome outcome, object? value = null) => outcome switch
    {
        ViewAccessOutcome.Allowed when value is not null => Results.Ok(value),
        ViewAccessOutcome.Unauthenticated => Unauthenticated(),
        _ => Missing(),
    };

    private static IResult Missing() => ApiErrors.NotFound("The View resource was not found.");
    private static IResult Unauthenticated() => ApiErrors.Problem(401, "Authentication required.", "A trusted View profile is required.");

    private static ViewScopeResolutionDto ToContract(ViewScopeResolution value) =>
        new(
            new ViewResolvedScopeDto(
                value.Scope.Kind,
                value.Scope.ProfileId,
                value.Scope.WasFallback),
            value.AvailableScopes
                .Select(option => new ViewScopeOptionDto(
                    option.Kind,
                    option.ProfileId,
                    option.Label,
                    option.AvatarColor,
                    option.AvatarUrl))
                .ToList());

    private static ViewPreferencesDto ToContract(ViewProfilePreferences value) =>
        new(
            value.ProfileId,
            value.LastScopeKind,
            value.LastScopeProfileId,
            value.TimelineDensity,
            value.UpdatedAt);

    private static ViewGalleryDto ToContract(ViewGallery value) =>
        new(value.Id, value.OwnerProfileId, value.PersonalSpaceId, value.Name,
            value.Description, value.Kind, value.SmartRuleJson, value.CoverItemId,
            value.SortOrder, value.ItemCount, value.CreatedAt, value.UpdatedAt);

    private static ViewGalleryItemPageDto ToContract(ViewGalleryItemPage value) =>
        new(value.Items.Select(item => new ViewGalleryItemDto(
                item.GalleryId, item.ItemId, item.Position, item.AddedAt)).ToList(),
            value.NextPosition, value.NextItemId, value.HasMore);

    private static AddViewGalleryItemsResponseDto ToContract(AddViewGalleryItemsResult value) =>
        new(value.Added, value.AlreadyPresent);

    private static ViewGalleryShareDto ToContract(ViewGalleryShare value) =>
        new(value.GalleryId, value.ProfileId, value.Permission, value.SharedAt);

}
