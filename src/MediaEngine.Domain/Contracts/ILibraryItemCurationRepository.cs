using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Contracts;

public interface ILibraryItemCurationRepository
{
    Task<LibraryItemTarget?> ResolveTargetAsync(Guid entityId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, LibraryItemTarget>> ResolveWorkTargetsAsync(
        IReadOnlyCollection<Guid> workIds,
        CancellationToken ct = default);

    Task UpsertCanonicalValuesAsync(
        Guid entityId,
        IReadOnlyCollection<MetadataClaim> claims,
        CancellationToken ct = default);

    Task MarkWorkRegisteredAsync(Guid workId, CancellationToken ct = default);

    Task CompletePendingReviewsAsync(
        Guid assetId,
        Guid workId,
        string status,
        string resolvedBy,
        DateTimeOffset resolvedAt,
        CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, LibraryItemRemovalTarget>> GetRemovalTargetsAsync(
        IReadOnlyCollection<Guid> workIds,
        CancellationToken ct = default);

    Task DeleteWorkRecordsAsync(LibraryItemRemovalTarget target, CancellationToken ct = default);
    Task<int> ApproveWorksAsync(IReadOnlyCollection<Guid> workIds, DateTimeOffset now, CancellationToken ct = default);

    Task MarkRejectedAsync(
        LibraryItemTarget target,
        string newFilePath,
        DateTimeOffset now,
        CancellationToken ct = default);

    Task<LibraryItemRecoveryResult?> RecoverAsync(Guid workId, DateTimeOffset now, CancellationToken ct = default);

    Task<LibraryItemProvisionalResult?> MarkProvisionalAsync(
        Guid workId,
        LibraryItemProvisionalMetadata metadata,
        DateTimeOffset now,
        CancellationToken ct = default);

    Task<IReadOnlyList<LibraryItemHistoryEntry>> GetHistoryAsync(
        Guid workId,
        CancellationToken ct = default);
}
