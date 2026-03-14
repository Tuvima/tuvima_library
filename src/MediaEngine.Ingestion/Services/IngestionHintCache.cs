using System.Collections.Concurrent;
using MediaEngine.Ingestion.Models;

namespace MediaEngine.Ingestion.Services;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IIngestionHintCache"/>.
/// Uses <see cref="ConcurrentDictionary{TKey,TValue}"/> for lock-free reads.
/// Hints expire after 24 hours and are purged on access or via <see cref="PurgeExpired"/>.
/// Singleton lifetime — not persisted to the database.
/// </summary>
public sealed class IngestionHintCache : IIngestionHintCache
{
    private readonly ConcurrentDictionary<string, FolderHint> _hints = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public bool TryGetHint(string folderPath, out FolderHint? hint)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            hint = null;
            return false;
        }

        var normalised = NormalisePath(folderPath);

        if (_hints.TryGetValue(normalised, out var cached))
        {
            if (cached.IsExpired)
            {
                // Lazy expiry: remove on access
                _hints.TryRemove(normalised, out _);
                hint = null;
                return false;
            }

            hint = cached;
            return true;
        }

        hint = null;
        return false;
    }

    /// <inheritdoc />
    public void SetHint(string folderPath, FolderHint hint)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return;
        _hints[NormalisePath(folderPath)] = hint;
    }

    /// <inheritdoc />
    public void InvalidateFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return;
        _hints.TryRemove(NormalisePath(folderPath), out _);
    }

    /// <inheritdoc />
    public void PurgeExpired()
    {
        foreach (var kvp in _hints)
        {
            if (kvp.Value.IsExpired)
                _hints.TryRemove(kvp.Key, out _);
        }
    }

    /// <summary>Normalises path separators for consistent dictionary keys.</summary>
    private static string NormalisePath(string path)
        => path.Replace('\\', '/').TrimEnd('/');
}
