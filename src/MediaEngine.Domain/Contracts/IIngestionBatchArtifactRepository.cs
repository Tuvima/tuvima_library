namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Records batch-scoped artifacts created by ingestion and enrichment workers.
/// </summary>
public interface IIngestionBatchArtifactRepository
{
    Task RecordAsync(
        Guid? batchId,
        string artifactType,
        Guid? artifactId,
        Guid? parentEntityId,
        string? parentEntityType,
        string action,
        string? displayName,
        string? providerId,
        string? source,
        string? detailJson,
        CancellationToken ct = default);
}
