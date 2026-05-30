using MediaEngine.Ingestion.Models;

namespace MediaEngine.Ingestion.Contracts;

/// <summary>
/// Best-effort writer for per-file ingestion lifecycle log entries and progress events.
/// </summary>
public interface IIngestionLogScribe
{
    Task<Guid> InsertDetectedAsync(
        IngestionCandidate candidate,
        Guid ingestionRunId,
        CancellationToken ct = default);

    Task UpdateStatusAsync(
        IngestionCandidate candidate,
        Guid logEntryId,
        string status,
        int progressPercent,
        bool isTerminal,
        CancellationToken ct = default,
        string? contentHash = null,
        string? mediaType = null,
        double? confidenceScore = null,
        string? detectedTitle = null,
        string? normalizedTitle = null,
        string? wikidataQid = null,
        Guid? mediaAssetId = null,
        string? errorDetail = null,
        string? title = null);

    Task RecordTerminalAsync(
        IngestionCandidate candidate,
        Guid? ingestionRunId,
        string status,
        string detail,
        CancellationToken ct = default);

    Task PublishProgressAsync(
        IngestionCandidate candidate,
        Guid logEntryId,
        string stage,
        int progressPercent,
        bool isTerminal,
        CancellationToken ct = default,
        Guid? mediaAssetId = null,
        string? title = null,
        string? mediaType = null);
}
