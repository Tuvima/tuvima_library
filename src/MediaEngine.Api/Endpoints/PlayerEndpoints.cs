using MediaEngine.Api.Security;
using MediaEngine.Api.Services.Playback;
using MediaEngine.Contracts.Playback;

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
                return Results.Conflict(new { error = ex.Message, ex.CurrentVersion, ex.ExpectedVersion });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("ReplacePlayerQueue")
        .WithSummary("Replace the queue and start playback from the requested item.")
        .Produces<PlayerStateDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status409Conflict)
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
                return Results.Conflict(new { error = ex.Message, ex.CurrentVersion, ex.ExpectedVersion });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("AddPlayerQueueItems")
        .WithSummary("Add works to the current queue at the end or next slot.")
        .Produces<PlayerStateDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status409Conflict)
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
                return Results.Conflict(new { error = ex.Message, ex.CurrentVersion, ex.ExpectedVersion });
            }
        })
        .WithName("ReorderPlayerQueue")
        .WithSummary("Persist a new queue order.")
        .Produces<PlayerStateDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status409Conflict)
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
                return Results.Conflict(new { error = ex.Message, ex.CurrentVersion, ex.ExpectedVersion });
            }
        })
        .WithName("RemovePlayerQueueItem")
        .WithSummary("Remove one queue item.")
        .Produces<PlayerStateDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status409Conflict)
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
                return Results.Conflict(new { error = ex.Message, ex.CurrentVersion, ex.ExpectedVersion });
            }
        })
        .WithName("ClearPlayerQueue")
        .WithSummary("Clear the queue and stop playback.")
        .Produces<PlayerStateDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status409Conflict)
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
                return Results.Conflict(new { error = ex.Message });
            }
        })
        .WithName("TakeOverPlayerSession")
        .WithSummary("Take control of a stale or explicitly forced player session from another client.")
        .Produces<PlayerStateDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status409Conflict)
        .RequireAnyRole();

        group.MapGet("/audiobooks/{workId:guid}/history", async (
            Guid workId,
            Guid? profileId,
            int? limit,
            PlayerService player,
            CancellationToken ct) =>
        {
            var history = await player.GetAudiobookHistoryAsync(profileId, workId, limit, ct);
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
                return Results.BadRequest(new { error = "An asset id is required for an audiobook bookmark." });
            }

            var bookmark = await player.CreateAudiobookBookmarkAsync(profileId, workId, request, ct);
            return Results.Created($"/player/audiobooks/{workId:D}/bookmarks/{bookmark.Id:D}", bookmark);
        })
        .WithName("CreateAudiobookBookmark")
        .WithSummary("Save the current audiobook playback position as a bookmark.")
        .Produces<AudiobookBookmarkDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAnyRole();

        group.MapDelete("/audiobooks/bookmarks/{bookmarkId:guid}", async (
            Guid bookmarkId,
            Guid? profileId,
            PlayerService player,
            CancellationToken ct) =>
        {
            var deleted = await player.DeleteAudiobookBookmarkAsync(profileId, bookmarkId, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteAudiobookBookmark")
        .WithSummary("Delete one saved audiobook bookmark.")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        return app;
    }
}
