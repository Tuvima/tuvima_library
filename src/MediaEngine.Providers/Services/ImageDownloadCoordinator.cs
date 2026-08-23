using System.Collections.Concurrent;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Serializes image persistence by source URL so concurrent ingestion,
/// enrichment, and manual rematch work cannot download the same image at the
/// same time. Callers must recheck durable state after acquiring the lease.
/// </summary>
public sealed class ImageDownloadCoordinator
{
    private readonly ConcurrentDictionary<string, LockEntry> _entries =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Shared fallback for directly constructed workers. Production resolves a
    /// singleton from DI, while tests and utility hosts still receive the same
    /// process-wide coordination behavior.
    /// </summary>
    public static ImageDownloadCoordinator Shared { get; } = new();

    public async ValueTask<IAsyncDisposable> AcquireAsync(
        string sourceUrl,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceUrl);
        var key = NormalizeSourceUrl(sourceUrl);

        LockEntry entry;
        while (true)
        {
            entry = _entries.GetOrAdd(key, static _ => new LockEntry());
            Interlocked.Increment(ref entry.ReferenceCount);

            if (_entries.TryGetValue(key, out var current)
                && ReferenceEquals(entry, current))
            {
                break;
            }

            Interlocked.Decrement(ref entry.ReferenceCount);
        }

        try
        {
            await entry.Gate.WaitAsync(ct).ConfigureAwait(false);
            return new Lease(this, key, entry);
        }
        catch
        {
            ReleaseReference(key, entry);
            throw;
        }
    }

    internal static string NormalizeSourceUrl(string sourceUrl)
    {
        var trimmed = sourceUrl.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return trimmed;

        var builder = new UriBuilder(uri)
        {
            Scheme = uri.Scheme.ToLowerInvariant(),
            Host = uri.Host.ToLowerInvariant(),
        };

        return builder.Uri.AbsoluteUri;
    }

    private void Release(string key, LockEntry entry)
    {
        entry.Gate.Release();
        ReleaseReference(key, entry);
    }

    private void ReleaseReference(string key, LockEntry entry)
    {
        if (Interlocked.Decrement(ref entry.ReferenceCount) == 0)
            _entries.TryRemove(new KeyValuePair<string, LockEntry>(key, entry));
    }

    private sealed class LockEntry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public int ReferenceCount;
    }

    private sealed class Lease(
        ImageDownloadCoordinator owner,
        string key,
        LockEntry entry) : IAsyncDisposable
    {
        private int _disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.Release(key, entry);

            return ValueTask.CompletedTask;
        }
    }
}
