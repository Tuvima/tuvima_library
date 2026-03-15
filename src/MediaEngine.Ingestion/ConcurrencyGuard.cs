using System.Collections.Concurrent;

namespace MediaEngine.Ingestion;

/// <summary>
/// Centralized concurrency guard that manages all lock dictionaries used by the
/// ingestion and enrichment pipelines. Documents and enforces the lock hierarchy
/// to prevent future deadlocks (Principle 5 — Gap Analysis Synthesis).
///
/// ──────────────────────────────────────────────────────────────────
/// Lock hierarchy (must acquire in this order to prevent deadlocks):
/// ──────────────────────────────────────────────────────────────────
///   1. Folder lock — broadest scope: serializes files in the same directory.
///   2. Hash lock   — within folder lock: prevents duplicate-check races on the same content.
///   3. QID lock    — independent (hydration pipeline only): serializes person merge per Wikidata QID.
///   4. Person lock — independent (identity service only): serializes person find-or-create per name+role.
///
/// QID and Person locks are never held simultaneously with Folder or Hash locks
/// (they run in separate pipeline stages), so no ordering constraint exists between groups {1,2} and {3,4}.
/// Within each group the ordering above MUST be respected.
///
/// Periodic cleanup: <see cref="Cleanup"/> removes all semaphores that are not currently held.
/// Callers should invoke this periodically (e.g. after a batch completes) to prevent unbounded growth.
/// </summary>
public sealed class ConcurrencyGuard
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _folderLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _hashLocks   = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _qidLocks    = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _personLocks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Acquires (or creates) the folder-level lock for the given folder key.</summary>
    public SemaphoreSlim GetFolderLock(string folderKey)
        => _folderLocks.GetOrAdd(folderKey, _ => new SemaphoreSlim(1, 1));

    /// <summary>Acquires (or creates) the hash-level lock. Must be called INSIDE a folder lock.</summary>
    public SemaphoreSlim GetHashLock(string hashHex)
        => _hashLocks.GetOrAdd(hashHex, _ => new SemaphoreSlim(1, 1));

    /// <summary>Releases and removes the hash lock entry after processing completes.</summary>
    public void ReleaseHashLock(string hashHex)
        => _hashLocks.TryRemove(hashHex, out _);

    /// <summary>Acquires (or creates) the QID-level lock for person merge operations.</summary>
    public SemaphoreSlim GetQidLock(string qid)
        => _qidLocks.GetOrAdd(qid, _ => new SemaphoreSlim(1, 1));

    /// <summary>Acquires (or creates) the person-level lock for find-or-create operations.</summary>
    public SemaphoreSlim GetPersonLock(string personKey)
        => _personLocks.GetOrAdd(personKey, _ => new SemaphoreSlim(1, 1));

    /// <summary>
    /// Removes all semaphores that are not currently held (CurrentCount == 1).
    /// Safe to call periodically to prevent unbounded dictionary growth.
    /// Returns the number of entries removed.
    /// </summary>
    public int Cleanup()
    {
        int removed = 0;
        removed += CleanupDictionary(_folderLocks);
        removed += CleanupDictionary(_hashLocks);
        removed += CleanupDictionary(_qidLocks);
        removed += CleanupDictionary(_personLocks);
        return removed;
    }

    /// <summary>Current number of tracked locks across all categories (for diagnostics).</summary>
    public int TotalTrackedLocks =>
        _folderLocks.Count + _hashLocks.Count + _qidLocks.Count + _personLocks.Count;

    private static int CleanupDictionary(ConcurrentDictionary<string, SemaphoreSlim> dict)
    {
        int removed = 0;
        foreach (var key in dict.Keys)
        {
            if (dict.TryGetValue(key, out var sem) && sem.CurrentCount == 1)
            {
                if (dict.TryRemove(key, out _))
                    removed++;
            }
        }
        return removed;
    }
}
