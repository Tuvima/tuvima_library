using MediaEngine.Api.Http;
using MediaEngine.Api.Security;
using MediaEngine.Contracts.Reading;
using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;

namespace MediaEngine.Api.Endpoints;

/// <summary>
/// EPUB reader data endpoints — CRUD for bookmarks, highlights, and reading statistics.
/// All endpoints use a hardcoded "local" user ID (multi-user deferred to Phase 16).
/// </summary>
public static class ReaderEndpoints
{
    private const string DefaultUserId = "local";

    public static IEndpointRouteBuilder MapReaderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/reader")
                       .WithTags("Reader Data");

        // ── Bookmarks ───────────────────────────────────────────────────────

        group.MapGet("/{assetId:guid}/bookmarks", async (
            Guid assetId,
            IReaderBookmarkRepository repo,
            CancellationToken ct) =>
        {
            var bookmarks = await repo.ListByAssetAsync(DefaultUserId, assetId, ct);
            return Results.Ok(bookmarks.Select(MapBookmark).ToList());
        })
        .WithName("ListBookmarks")
        .WithSummary("Lists all bookmarks for the given asset.")
        .Produces<IReadOnlyList<ReaderBookmarkDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        group.MapPost("/{assetId:guid}/bookmarks", async (
            Guid assetId,
            CreateReaderBookmarkRequestDto request,
            IReaderBookmarkRepository repo,
            CancellationToken ct) =>
        {
            var bookmark = new ReaderBookmark
            {
                Id           = Guid.NewGuid(),
                UserId       = DefaultUserId,
                AssetId      = assetId,
                ChapterIndex = request.ChapterIndex,
                CfiPosition  = request.CfiPosition,
                Label        = request.Label,
                CreatedAt    = DateTime.UtcNow
            };

            await repo.InsertAsync(bookmark, ct);
            return Results.Created($"/reader/bookmarks/{bookmark.Id}", MapBookmark(bookmark));
        })
        .WithName("CreateBookmark")
        .WithSummary("Creates a bookmark at the specified chapter position.")
        .Produces<ReaderBookmarkDto>(StatusCodes.Status201Created)
        .RequireAnyRole();

        group.MapDelete("/bookmarks/{id:guid}", async (
            Guid id,
            IReaderBookmarkRepository repo,
            CancellationToken ct) =>
        {
            var existing = await repo.FindByIdAsync(id, ct);
            if (existing is null)
                return ApiErrors.NotFound($"Bookmark '{id}' not found.");

            await repo.DeleteAsync(id, ct);
            return Results.NoContent();
        })
        .WithName("DeleteBookmark")
        .WithSummary("Deletes a bookmark by ID.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        // ── Highlights ──────────────────────────────────────────────────────

        group.MapGet("/{assetId:guid}/highlights", async (
            Guid assetId,
            IReaderHighlightRepository repo,
            CancellationToken ct) =>
        {
            var highlights = await repo.ListByAssetAsync(DefaultUserId, assetId, ct);
            return Results.Ok(highlights.Select(MapHighlight).ToList());
        })
        .WithName("ListHighlights")
        .WithSummary("Lists all highlights for the given asset.")
        .Produces<IReadOnlyList<ReaderHighlightDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        group.MapPost("/{assetId:guid}/highlights", async (
            Guid assetId,
            CreateReaderHighlightRequestDto request,
            IReaderHighlightRepository repo,
            CancellationToken ct) =>
        {
            var highlight = new ReaderHighlight
            {
                Id           = Guid.NewGuid(),
                UserId       = DefaultUserId,
                AssetId      = assetId,
                ChapterIndex = request.ChapterIndex,
                StartOffset  = request.StartOffset,
                EndOffset    = request.EndOffset,
                SelectedText = request.SelectedText,
                Color        = request.Color ?? HighlightColor.Yellow,
                NoteText     = request.NoteText,
                CreatedAt    = DateTime.UtcNow
            };

            await repo.InsertAsync(highlight, ct);
            return Results.Created($"/reader/highlights/{highlight.Id}", MapHighlight(highlight));
        })
        .WithName("CreateHighlight")
        .WithSummary("Creates a text highlight with optional note and colour.")
        .Produces<ReaderHighlightDto>(StatusCodes.Status201Created)
        .RequireAnyRole();

        group.MapPut("/highlights/{id:guid}", async (
            Guid id,
            UpdateReaderHighlightRequestDto request,
            IReaderHighlightRepository repo,
            CancellationToken ct) =>
        {
            var existing = await repo.FindByIdAsync(id, ct);
            if (existing is null)
                return ApiErrors.NotFound($"Highlight '{id}' not found.");

            await repo.UpdateAsync(id, request.Color, request.NoteText, ct);
            return Results.NoContent();
        })
        .WithName("UpdateHighlight")
        .WithSummary("Updates a highlight's colour and/or note text.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapDelete("/highlights/{id:guid}", async (
            Guid id,
            IReaderHighlightRepository repo,
            CancellationToken ct) =>
        {
            var existing = await repo.FindByIdAsync(id, ct);
            if (existing is null)
                return ApiErrors.NotFound($"Highlight '{id}' not found.");

            await repo.DeleteAsync(id, ct);
            return Results.NoContent();
        })
        .WithName("DeleteHighlight")
        .WithSummary("Deletes a highlight by ID.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        // ── Statistics ──────────────────────────────────────────────────────

        group.MapGet("/{assetId:guid}/statistics", async (
            Guid assetId,
            IReaderStatisticsRepository repo,
            CancellationToken ct) =>
        {
            var stats = await repo.GetAsync(DefaultUserId, assetId, ct);
            if (stats is null)
                return Results.Ok(new ReaderStatisticsDto
                {
                    Id      = Guid.NewGuid(),
                    UserId  = DefaultUserId,
                    AssetId = assetId
                });

            return Results.Ok(MapStatistics(stats));
        })
        .WithName("GetReadingStatistics")
        .WithSummary("Returns reading statistics for the given asset (or defaults if none exist).")
        .Produces<ReaderStatisticsDto>(StatusCodes.Status200OK)
        .RequireAnyRole();

        group.MapPut("/{assetId:guid}/statistics", async (
            Guid assetId,
            UpdateReaderStatisticsRequestDto request,
            IReaderStatisticsRepository repo,
            CancellationToken ct) =>
        {
            var stats = await repo.GetAsync(DefaultUserId, assetId, ct) ?? new ReaderStatistics
            {
                Id      = Guid.NewGuid(),
                UserId  = DefaultUserId,
                AssetId = assetId
            };

            stats.ChaptersRead         = request.ChaptersRead;
            stats.TotalReadingTimeSecs  = request.TotalReadingTimeSecs;
            stats.WordsRead            = request.WordsRead;
            stats.SessionsCount        = request.SessionsCount;
            stats.AvgWordsPerMinute     = request.AvgWordsPerMinute;
            stats.LastSessionAt         = DateTime.UtcNow;

            await repo.UpsertAsync(stats, ct);
            return Results.NoContent();
        })
        .WithName("UpdateReadingStatistics")
        .WithSummary("Upserts reading statistics (auto-saved by the reader every 30 seconds).")
        .Produces(StatusCodes.Status204NoContent)
        .RequireAnyRole();

        // -- WhisperSync Alignment -----------------------------------------------

        group.MapPost("/{assetId:guid}/whispersync", async (
            Guid assetId,
            CreateAlignmentRequestDto request,
            IWhisperSyncService whisperSync,
            CancellationToken ct) =>
        {
            var job = await whisperSync.CreateAlignmentJobAsync(assetId, request.AudiobookAssetId, ct);
            return Results.Created($"/reader/whispersync/{job.Id}", MapAlignmentJob(job));
        })
        .WithName("CreateWhisperSyncJob")
        .WithSummary("Creates an ebook-to-audiobook alignment job.")
        .Produces<AlignmentJobDto>(StatusCodes.Status201Created)
        .RequireAnyRole();

        group.MapGet("/{assetId:guid}/whispersync", async (
            Guid assetId,
            IWhisperSyncService whisperSync,
            CancellationToken ct) =>
        {
            var jobs = await whisperSync.GetJobsForAssetAsync(assetId, ct);
            return Results.Ok(jobs.Select(MapAlignmentJob).ToList());
        })
        .WithName("GetWhisperSyncJobs")
        .WithSummary("Gets alignment job status for an ebook asset.")
        .Produces<IReadOnlyList<AlignmentJobDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        group.MapDelete("/whispersync/{jobId:guid}", async (
            Guid jobId,
            IWhisperSyncService whisperSync,
            CancellationToken ct) =>
        {
            var cancelled = await whisperSync.CancelJobAsync(jobId, ct);
            return cancelled ? Results.NoContent() : ApiErrors.NotFound($"Job '{jobId}' not found or already completed.");
        })
        .WithName("CancelWhisperSyncJob")
        .WithSummary("Cancels a pending alignment job.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        return app;
    }

    private static ReaderBookmarkDto MapBookmark(ReaderBookmark bookmark) => new()
    {
        Id = bookmark.Id,
        UserId = bookmark.UserId,
        AssetId = bookmark.AssetId,
        ChapterIndex = bookmark.ChapterIndex,
        CfiPosition = bookmark.CfiPosition,
        Label = bookmark.Label,
        CreatedAt = bookmark.CreatedAt,
    };

    private static ReaderHighlightDto MapHighlight(ReaderHighlight highlight) => new()
    {
        Id = highlight.Id,
        UserId = highlight.UserId,
        AssetId = highlight.AssetId,
        ChapterIndex = highlight.ChapterIndex,
        StartOffset = highlight.StartOffset,
        EndOffset = highlight.EndOffset,
        SelectedText = highlight.SelectedText,
        Color = highlight.Color,
        NoteText = highlight.NoteText,
        CreatedAt = highlight.CreatedAt,
    };

    private static ReaderStatisticsDto MapStatistics(ReaderStatistics statistics) => new()
    {
        Id = statistics.Id,
        UserId = statistics.UserId,
        AssetId = statistics.AssetId,
        ChaptersRead = statistics.ChaptersRead,
        TotalReadingTimeSecs = statistics.TotalReadingTimeSecs,
        WordsRead = statistics.WordsRead,
        SessionsCount = statistics.SessionsCount,
        AvgWordsPerMinute = statistics.AvgWordsPerMinute,
        LastSessionAt = statistics.LastSessionAt,
    };

    private static AlignmentJobDto MapAlignmentJob(AlignmentJob job) => new()
    {
        Id = job.Id,
        EbookAssetId = job.EbookAssetId,
        AudiobookAssetId = job.AudiobookAssetId,
        Status = job.Status,
        AlignmentData = job.AlignmentData,
        ErrorMessage = job.ErrorMessage,
        CreatedAt = job.CreatedAt,
        CompletedAt = job.CompletedAt,
    };
}

