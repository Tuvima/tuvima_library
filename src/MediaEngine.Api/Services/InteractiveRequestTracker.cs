using System.Diagnostics;

namespace MediaEngine.Api.Services;

/// <summary>
/// Tracks user-facing Engine requests so opportunistic background work can yield
/// resources while the Dashboard, playback, or another API client is active.
/// </summary>
public sealed class InteractiveRequestTracker
{
    private long _lastActivityTimestamp = Stopwatch.GetTimestamp();
    private int _activeRequests;

    public event Action? RequestStarted;

    public int ActiveRequests => Volatile.Read(ref _activeRequests);

    public TimeSpan TimeSinceLastActivity =>
        Stopwatch.GetElapsedTime(Volatile.Read(ref _lastActivityTimestamp));

    public bool HasPressure(TimeSpan quietPeriod) =>
        ActiveRequests > 0 || TimeSinceLastActivity < quietPeriod;

    public IDisposable Begin()
    {
        Volatile.Write(ref _lastActivityTimestamp, Stopwatch.GetTimestamp());
        Interlocked.Increment(ref _activeRequests);
        PerformanceMetrics.ActiveInteractiveRequests.Add(1);
        RequestStarted?.Invoke();
        return new RequestLease(this, Stopwatch.GetTimestamp());
    }

    private void Complete(long startedAt)
    {
        Volatile.Write(ref _lastActivityTimestamp, Stopwatch.GetTimestamp());
        Interlocked.Decrement(ref _activeRequests);
        PerformanceMetrics.ActiveInteractiveRequests.Add(-1);
        PerformanceMetrics.InteractiveRequestDurationMs.Record(
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
    }

    private sealed class RequestLease(InteractiveRequestTracker owner, long startedAt) : IDisposable
    {
        private InteractiveRequestTracker? _owner = owner;

        public void Dispose() =>
            Interlocked.Exchange(ref _owner, null)?.Complete(startedAt);
    }
}
