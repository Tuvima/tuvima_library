using MediaEngine.Ingestion.Models;
using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Configuration;
using MediaEngine.Domain.Enums;
using MediaEngine.Ingestion.Pipeline;
using MediaEngine.Ingestion.Services;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Ingestion;

public sealed partial class IngestionEngine
{
    private IntakeContext? ResolveWatcherIntakeContext(string path)
        => _options.ResolveIncomingIntakeContext(path);

    private void ApplySharedIncomingRouting(
        IngestionPipelineContext context,
        MediaType resolvedMediaType)
    {
        var candidate = context.Candidate;
        var routing = SharedIncomingRouter.Route(
            candidate.Intake,
            resolvedMediaType,
            _options.LibraryFolders);

        if (routing.IsResolved)
        {
            context.Library = routing.Library;
            candidate.Intake = candidate.Intake! with
            {
                DestinationLibraryId = routing.Library!.Id,
            };
            _logger.LogInformation(
                "Shared incoming source {SourceId} routed {FileName} to library {LibraryName} ({LibraryId})",
                candidate.Intake.SourceId,
                Path.GetFileName(candidate.Path),
                routing.Library.Name,
                routing.Library.Id);
            return;
        }

        if (!routing.Applies)
            return;

        context.IntakeRoutingFailure = routing.FailureReason
            ?? "The shared incoming destination could not be resolved.";
        _logger.LogWarning(
            "Shared incoming source {SourceId} could not route {FileName}: {Reason}",
            candidate.Intake?.SourceId,
            Path.GetFileName(candidate.Path),
            context.IntakeRoutingFailure);
    }

    private Task CreateUnresolvedIntakeReviewAsync(
        IngestionPipelineContext context,
        CancellationToken ct) =>
        CreateIngestionReviewItemAsync(
            context.AssetId,
            ReviewTrigger.UnresolvedIntakeDestination,
            context.MediaTypeCandidates.FirstOrDefault()?.Confidence ?? 0.0,
            context.IntakeRoutingFailure!,
            ct,
            context.IngestionRunId);

    private async Task BlockUnresolvedIncomingAsync(
        IngestionPipelineContext context,
        CancellationToken ct)
    {
        var candidate = context.Candidate;
        var reason = context.IntakeRoutingFailure!;

        try
        {
            await _ingestionLog.UpdateStatusAsync(
                context.LogEntryId,
                "needs_review",
                mediaType: context.ResolvedMediaType.ToString(),
                mediaAssetId: context.AssetId,
                errorDetail: reason,
                ct: ct).ConfigureAwait(false);
            await PublishItemProgressAsync(
                candidate,
                context.LogEntryId,
                "needs_review",
                100,
                true,
                ct,
                context.AssetId,
                context.ResolvedTitle,
                context.ResolvedMediaType.ToString()).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Failed to publish unresolved incoming review state");
        }

        if (candidate.BatchId.HasValue)
        {
            await SafeIncrementBatchCounterAsync(candidate.BatchId.Value, BatchCounterColumn.FilesFailed, ct).ConfigureAwait(false);
            await SafeIncrementBatchCounterAsync(candidate.BatchId.Value, BatchCounterColumn.FilesProcessed, ct).ConfigureAwait(false);
            await PublishQueuedBatchSnapshotAsync(candidate.BatchId.Value, ct).ConfigureAwait(false);
        }

        await MarkBlockedOperationAsync(context.DurableOperation, reason, ct).ConfigureAwait(false);
        _logger.LogWarning(
            "Shared incoming file {FileName} remains in place pending destination review",
            Path.GetFileName(candidate.Path));
    }

    /// <inheritdoc />
    public Task EnqueueIntakeAsync(IntakeFileRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        if (!IntakeSourceKinds.IsValid(request.SourceKind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request), request.SourceKind, "Unsupported intake source kind.");
        }

        var fullPath = Path.GetFullPath(request.Path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The intake file does not exist.", fullPath);
        }

        var destination = string.IsNullOrWhiteSpace(request.DestinationLibraryId)
            ? null
            : _libraryFolderResolver?.ResolveById(request.DestinationLibraryId);
        if (!string.IsNullOrWhiteSpace(request.DestinationLibraryId) && destination is null)
        {
            throw new InvalidOperationException(
                $"Direct intake destination library '{request.DestinationLibraryId}' is not configured.");
        }

        if (destination is not null
            && (string.Equals(destination.Kind, LibraryKinds.Personal, StringComparison.OrdinalIgnoreCase)
                || string.Equals(destination.Area, LibraryAreas.View, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Personal View library '{destination.Name}' must use the local-asset intake path; "
                + "catalogue ingestion is not permitted for this destination.");
        }

        _debounce.Enqueue(new FileEvent
        {
            Path = fullPath,
            EventType = FileEventType.Created,
            OccurredAt = DateTimeOffset.UtcNow,
            BatchId = request.BatchId,
            Intake = new IntakeContext
            {
                SourceKind = request.SourceKind,
                SourceId = request.SourceId,
                DestinationLibraryId = request.DestinationLibraryId,
                ActorProfileId = request.ActorProfileId,
            },
        });

        return Task.CompletedTask;
    }
}
