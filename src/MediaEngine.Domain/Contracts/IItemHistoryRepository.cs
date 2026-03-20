namespace MediaEngine.Domain.Contracts;

using MediaEngine.Domain.Entities;

/// <summary>
/// Append-only event log per media item.
/// </summary>
public interface IItemHistoryRepository
{
    /// <summary>Append a new history entry.</summary>
    Task AppendAsync(Guid entityId, string eventType, string label, string? detail = null, CancellationToken ct = default);

    /// <summary>Get all history entries for an entity, ordered newest-first.</summary>
    Task<IReadOnlyList<ItemHistoryEntry>> GetHistoryAsync(Guid entityId, CancellationToken ct = default);
}
