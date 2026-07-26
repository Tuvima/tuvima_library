using MediaEngine.Api.Http;
using MediaEngine.Api.Security;
using MediaEngine.Api.Services.Playback;
using MediaEngine.Contracts.Paging;
using MediaEngine.Contracts.Playback;
using MediaEngine.Storage.Playback;

namespace MediaEngine.Api.Endpoints;

public static class PlayerEndpoints
{
    public static IEndpointRouteBuilder MapPlayerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/player")
            .WithTags("Player");

        group.MapGet("/capabilities", (PlayerService player) =>
            Results.Ok(player.GetCapabilities()))
        .WithName("GetPlayerCapabilities")
        .WithSummary("Return capabilities for Engine-backed player clients.")
        .Produces<PlayerCapabilitiesDto>(StatusCodes.Status200OK)
        .RequireAnyRole();

        group.MapGet("/state", async (
            Guid? profileId,
            string? deviceId,
            string? client,
            PlayerService player,
            CancellationToken ct) =>
        {
            var state = await player.GetStateAsync(profileId, deviceId, client, ct);
            return Results.Ok(state);
        })
        .WithName("GetPlayerState")
        .WithSummary("Return the current player session state and queue.")
        .Produces<PlayerStateDto>(StatusCodes.Status200OK)
        .RequireAnyRole();

        group.MapPost("/queue/replace", async (
            PlayerQueueMutationDto request,
            PlayerService player,
            CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await player.ReplaceQueueAsync(request, ct));
            }
            catch (PlayerStateConflictException ex)
            {
                return ApiErrors.Conflict(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return ApiErrors.BadRequest(ex.Message);
            }
        })
        .WithName("ReplacePlayerQueue")
        .WithSummary("Replace the queue and start playback from the requested item.")
        .Produces<PlayerStateDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAnyRole();

        group.MapPost("/queue/items", async (
            PlayerQueueMutationDto request,
            PlayerService player,
            CancellationToken ct) =>
        {
            try
            {
                var insertNext = string.Equals(request.Mode, PlayerQueueMutationModes.AddNext, StringComparison.OrdinalIgnoreCase);
                return Results.Ok(await player.AddQueueItemsAsync(request, insertNext, ct));
            }
            catch (PlayerStateConflictException ex)
            {
                return ApiErrors.Conflict(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return ApiErrors.BadRequest(ex.Message);
            }
        })
        .WithName("AddPlayerQueueItems")
        .WithSummary("Add works to the current queue at the end or next slot.")
        .Produces<PlayerStateDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAnyRole();

        group.MapMethods("/queue/order", ["PUT", "POST"], async (
            PlayerQueueMutationDto request,
            PlayerService player,
            CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await player.ReorderQueueAsync(request, ct));
            }
            catch (PlayerStateConflictException ex)
            {
                return ApiErrors.Conflict(ex.Message);
            }
        })
        .WithName("ReorderPlayerQueue")
        .WithSummary("Persist a new queue order.")
        .Produces<PlayerStateDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAnyRole();

        group.MapDelete("/queue/items/{queueItemId:guid}", async (
            Guid queueItemId,
            Guid? profileId,
            string? deviceId,
            string? client,
            long? expectedStateVersion,
            bool? force,
            PlayerService player,
            CancellationToken ct) =>
        {
            try
            {
                var request = new PlayerQueueMutationDto
                {
                    ProfileId = profileId,
                    DeviceId = deviceId,
                    Client = client,
                    ExpectedStateVersion = expectedStateVersion,
                    Force = force.GetValueOrDefault(),
                };
                return Results.Ok(await player.RemoveQueueItemAsync(queueItemId, request, ct));
            }
            catch (PlayerStateConflictException ex)
            {
                return ApiErrors.Conflict(ex.Message);
            }
        })
        .WithName("RemovePlayerQueueItem")
        .WithSummary("Remove one queue item.")
        .Produces<PlayerStateDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAnyRole();

        group.MapDelete("/queue", async (
            Guid? profileId,
            string? deviceId,
            string? client,
            long? expectedStateVersion,
            bool? force,
            PlayerService player,
            CancellationToken ct) =>
        {
            try
            {
                var request = new PlayerQueueMutationDto
                {
                    ProfileId = profileId,
                    DeviceId = deviceId,
                    Client = client,
                    ExpectedStateVersion = expectedStateVersion,
                    Force = force.GetValueOrDefault(),
                };
                return Results.Ok(await player.ClearQueueAsync(request, ct));
            }
            catch (PlayerStateConflictException ex)
            {
                return ApiErrors.Conflict(ex.Message);
            }
        })
        .WithName("ClearPlayerQueue")
        .WithSummary("Clear the queue and stop playback.")
        .Produces<PlayerStateDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAnyRole();

        group.MapPost("/command", async (
            PlayerCommandRequestDto request,
            PlayerService player,
            CancellationToken ct) =>
        {
            var state = await player.ApplyCommandAsync(request, ct);
            return Results.Ok(state);
        })
        .WithName("SendPlayerCommand")
        .WithSummary("Send a playback command such as play, pause, next, seek, volume, speed, shuffle, or repeat.")
        .Produces<PlayerStateDto>(StatusCodes.Status200OK)
        .RequireAnyRole();

        group.MapPost("/heartbeat", async (
            PlayerHeartbeatDto request,
            PlayerService player,
            CancellationToken ct) =>
        {
            var state = await player.HeartbeatAsync(request, ct);
            return Results.Ok(state);
        })
        .WithName("PostPlayerHeartbeat")
        .WithSummary("Update active player timing and persist exact resume progress.")
        .Produces<PlayerStateDto>(StatusCodes.Status200OK)
        .RequireAnyRole();

        group.MapPost("/session/takeover", async (
            PlayerSessionTakeoverRequestDto request,
            PlayerService player,
            CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await player.TakeoverAsync(request, ct));
            }
            catch (PlayerSessionConflictException ex)
            {
                return ApiErrors.Conflict(ex.Message);
            }
        })
        .WithName("TakeOverPlayerSession")
        .WithSummary("Take control of a stale or explicitly forced player session from another client.")
        .Produces<PlayerStateDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAnyRole();

        group.MapGet("/audiobooks/{workId:guid}/history", async (
            Guid workId,
            Guid? profileId,
            int? limit,
            PlayerService player,
            CancellationToken ct) =>
        {
            // A caller-supplied limit is clamped to PagedRequest.MaxLimit; when absent, null is
            // preserved so the service falls back to the profile's configured history limit.
            var clampedLimit = limit.HasValue ? PagedRequest.From(null, limit).Limit : (int?)null;
            var history = await player.GetAudiobookHistoryAsync(profileId, workId, clampedLimit, ct);
            return Results.Ok(history);
        })
        .WithName("GetAudiobookListenHistory")
        .WithSummary("Return recent qualified audiobook listen checkpoints for resume recovery.")
        .Produces<IReadOnlyList<AudiobookListenHistoryItemDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        group.MapGet("/audiobooks/{workId:guid}/bookmarks", async (
            Guid workId,
            Guid? profileId,
            PlayerService player,
            CancellationToken ct) =>
        {
            var bookmarks = await player.GetAudiobookBookmarksAsync(profileId, workId, ct);
            return Results.Ok(bookmarks);
        })
        .WithName("GetAudiobookBookmarks")
        .WithSummary("Return saved audiobook playback bookmarks for a work.")
        .Produces<IReadOnlyList<AudiobookBookmarkDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        group.MapPost("/audiobooks/{workId:guid}/bookmarks", async (
            Guid workId,
            Guid? profileId,
            CreateAudiobookBookmarkRequestDto request,
            PlayerService player,
            CancellationToken ct) =>
        {
            if (request.AssetId == Guid.Empty)
            {
                return ApiErrors.BadRequest("An asset id is required for an audiobook bookmark.");
            }

            var bookmark = await player.CreateAudiobookBookmarkAsync(profileId, workId, request, ct);
            return Results.Created($"/player/audiobooks/{workId:D}/bookmarks/{bookmark.Id:D}", bookmark);
        })
        .WithName("CreateAudiobookBookmark")
        .WithSummary("Save the current audiobook playback position as a bookmark.")
        .Produces<AudiobookBookmarkDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAnyRole();

        group.MapDelete("/audiobooks/bookmarks/{bookmarkId:guid}", async (
            Guid bookmarkId,
            Guid? profileId,
            PlayerService player,
            CancellationToken ct) =>
        {
            var deleted = await player.DeleteAudiobookBookmarkAsync(profileId, bookmarkId, ct);
            return deleted ? Results.NoContent() : ApiErrors.NotFound($"Audiobook bookmark '{bookmarkId}' not found.");
        })
        .WithName("DeleteAudiobookBookmark")
        .WithSummary("Delete one saved audiobook bookmark.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapPost("/audiobooks/{workId:guid}/chapters/suggest-names", async (
            Guid workId,
            SuggestAudiobookChapterNamesRequestDto request,
            AudiobookChapterNamingService naming,
            CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await naming.SuggestNamesAsync(workId, request, ct));
            }
            catch (KeyNotFoundException ex)
            {
                return ApiErrors.NotFound(ex.Message);
            }
        })
        .WithName("SuggestAudiobookChapterNames")
        .WithSummary("Suggest display-only audiobook chapter names using local AI.")
        .Produces<AudiobookChapterNameSuggestionsDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapGet("/audiobooks/{workId:guid}/chapter-overrides", async (
            Guid workId,
            Guid? assetId,
            AudiobookChapterNamingService naming,
            CancellationToken ct) =>
        {
            var overrides = await naming.GetOverridesAsync(workId, assetId, ct);
            return Results.Ok(overrides);
        })
        .WithName("GetAudiobookChapterTitleOverrides")
        .WithSummary("Return display-only audiobook chapter title overrides.")
        .Produces<IReadOnlyList<AudiobookChapterTitleOverrideDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        group.MapPost("/audiobooks/{workId:guid}/chapter-overrides", async (
            Guid workId,
            UpsertAudiobookChapterTitleOverrideRequestDto request,
            AudiobookChapterNamingService naming,
            CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await naming.UpsertOverrideAsync(workId, request, ct));
            }
            catch (ArgumentException ex)
            {
                return ApiErrors.BadRequest(ex.Message);
            }
            catch (KeyNotFoundException ex)
            {
                return ApiErrors.NotFound(ex.Message);
            }
        })
        .WithName("UpsertAudiobookChapterTitleOverride")
        .WithSummary("Create or update one display-only audiobook chapter title override.")
        .Produces<AudiobookChapterTitleOverrideDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapDelete("/audiobooks/{workId:guid}/chapter-overrides/{assetId:guid}/{chapterIndex:int}", async (
            Guid workId,
            Guid assetId,
            int chapterIndex,
            AudiobookChapterNamingService naming,
            CancellationToken ct) =>
        {
            var deleted = await naming.DeleteOverrideAsync(workId, assetId, chapterIndex, ct);
            return deleted
                ? Results.NoContent()
                : ApiErrors.NotFound($"Chapter title override not found for work '{workId}', asset '{assetId}', chapter {chapterIndex}.");
        })
        .WithName("DeleteAudiobookChapterTitleOverride")
        .WithSummary("Delete one display-only audiobook chapter title override.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        return app;
    }
}
