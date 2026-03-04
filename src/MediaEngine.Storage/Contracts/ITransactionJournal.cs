namespace MediaEngine.Storage.Contracts;

/// <summary>
/// Provides methods for logging system-level entity changes.
/// Spec: Phase 4 – Interfaces § ITransactionJournal
/// </summary>
public interface ITransactionJournal
{
    /// <summary>
    /// Appends a row to <c>transaction_log</c>.
    /// </summary>
    /// <param name="eventType">High-level event name, e.g. "HUB_CREATED".</param>
    /// <param name="entityType">Entity kind, e.g. "Hub", "Work", "MediaAsset".</param>
    /// <param name="entityId">UUID of the affected entity.</param>
    void Log(string eventType, string entityType, string entityId);

    /// <summary>
    /// Removes the oldest entries so that at most <paramref name="maxEntries"/> rows remain.
    /// Spec: "SHOULD be archived or truncated after reaching 100,000 entries."
    /// </summary>
    void Prune(int maxEntries = 100_000);
}
