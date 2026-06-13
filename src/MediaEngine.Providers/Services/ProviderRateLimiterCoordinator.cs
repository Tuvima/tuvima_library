using System.Collections.Concurrent;
using System.Diagnostics;
using MediaEngine.Storage.Models;

namespace MediaEngine.Providers.Services;

public interface IProviderRateLimiterCoordinator
{
    Task<T> ExecuteAsync<T>(
        string providerName,
        ProviderRateLimitConfiguration? rateLimit,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken ct);

    Task ExecuteAsync(
        string providerName,
        ProviderRateLimitConfiguration? rateLimit,
        Func<CancellationToken, Task> operation,
        CancellationToken ct);

    IReadOnlyList<ProviderActivitySnapshot> GetSnapshots();
}

public sealed record ProviderActivitySnapshot(
    string ProviderName,
    int ActiveRequests,
    long RequestsTotal,
    int RequestsLastMinute,
    long ErrorsTotal,
    int ErrorsLastMinute,
    long ThrottleWaitMsTotal,
    double AverageLatencyMs,
    DateTimeOffset? LastRequestAt,
    string? LastError);

public sealed class ProviderRateLimiterCoordinator : IProviderRateLimiterCoordinator
{
    private readonly ConcurrentDictionary<string, ProviderRequestLimiter> _limiters = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ProviderActivityCounter> _activity = new(StringComparer.OrdinalIgnoreCase);

    public async Task<T> ExecuteAsync<T>(
        string providerName,
        ProviderRateLimitConfiguration? rateLimit,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentNullException.ThrowIfNull(operation);

        var limiter = _limiters.GetOrAdd(providerName, _ => new ProviderRequestLimiter(rateLimit));
        var activity = _activity.GetOrAdd(providerName, name => new ProviderActivityCounter(name));

        await using var lease = await limiter.WaitAsync(ct).ConfigureAwait(false);
        activity.RecordStarted(lease.WaitDuration);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await operation(ct).ConfigureAwait(false);
            activity.RecordCompleted(stopwatch.Elapsed, error: null);
            return result;
        }
        catch (OperationCanceledException)
        {
            activity.RecordCompleted(stopwatch.Elapsed, error: null);
            throw;
        }
        catch (Exception ex)
        {
            activity.RecordCompleted(stopwatch.Elapsed, ex);
            throw;
        }
    }

    public async Task ExecuteAsync(
        string providerName,
        ProviderRateLimitConfiguration? rateLimit,
        Func<CancellationToken, Task> operation,
        CancellationToken ct)
    {
        await ExecuteAsync(
            providerName,
            rateLimit,
            async token =>
            {
                await operation(token).ConfigureAwait(false);
                return true;
            },
            ct).ConfigureAwait(false);
    }

    public IReadOnlyList<ProviderActivitySnapshot> GetSnapshots() =>
        _activity.Values
            .Select(counter => counter.Snapshot(DateTimeOffset.UtcNow))
            .OrderBy(snapshot => snapshot.ProviderName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private sealed class ProviderRequestLimiter
    {
        private readonly SemaphoreSlim _concurrency;
        private readonly object _tokenLock = new();
        private readonly double _requestsPerSecond;
        private readonly int _burst;
        private double _tokens;
        private DateTimeOffset _lastRefillUtc;

        public ProviderRequestLimiter(ProviderRateLimitConfiguration? rateLimit)
        {
            var maxConcurrency = Math.Max(1, rateLimit?.MaxConcurrency ?? 1);
            _concurrency = new SemaphoreSlim(maxConcurrency, maxConcurrency);

            _requestsPerSecond = ResolveRatePerSecond(rateLimit);
            _burst = Math.Max(1, rateLimit?.Burst ?? maxConcurrency);
            _tokens = _burst;
            _lastRefillUtc = DateTimeOffset.UtcNow;
        }

        public async Task<ProviderRateLimitLease> WaitAsync(CancellationToken ct)
        {
            var stopwatch = Stopwatch.StartNew();
            await _concurrency.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await WaitForTokenAsync(ct).ConfigureAwait(false);
                return new ProviderRateLimitLease(this, stopwatch.Elapsed);
            }
            catch
            {
                _concurrency.Release();
                throw;
            }
        }

        private async Task WaitForTokenAsync(CancellationToken ct)
        {
            if (_requestsPerSecond <= 0)
                return;

            while (true)
            {
                TimeSpan delay;
                lock (_tokenLock)
                {
                    RefillTokens(DateTimeOffset.UtcNow);
                    if (_tokens >= 1)
                    {
                        _tokens -= 1;
                        return;
                    }

                    var missingTokens = 1 - _tokens;
                    delay = TimeSpan.FromSeconds(missingTokens / _requestsPerSecond);
                    if (delay < TimeSpan.FromMilliseconds(25))
                        delay = TimeSpan.FromMilliseconds(25);
                }

                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }

        private void RefillTokens(DateTimeOffset now)
        {
            var elapsedSeconds = (now - _lastRefillUtc).TotalSeconds;
            if (elapsedSeconds <= 0)
                return;

            _tokens = Math.Min(_burst, _tokens + elapsedSeconds * _requestsPerSecond);
            _lastRefillUtc = now;
        }

        private static double ResolveRatePerSecond(ProviderRateLimitConfiguration? rateLimit)
        {
            if (rateLimit?.RequestsPerSecond is > 0)
                return rateLimit.RequestsPerSecond.Value;

            if (rateLimit?.RequestsPerMinute is > 0)
                return rateLimit.RequestsPerMinute.Value / 60.0;

            return 0;
        }

        public void Release() => _concurrency.Release();
    }

    private sealed class ProviderRateLimitLease : IAsyncDisposable
    {
        private readonly ProviderRequestLimiter _owner;
        private bool _disposed;

        public ProviderRateLimitLease(ProviderRequestLimiter owner, TimeSpan waitDuration)
        {
            _owner = owner;
            WaitDuration = waitDuration;
        }

        public TimeSpan WaitDuration { get; }

        public ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                _owner.Release();
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class ProviderActivityCounter
    {
        private readonly ConcurrentQueue<DateTimeOffset> _requestStarts = new();
        private readonly ConcurrentQueue<DateTimeOffset> _errorStarts = new();
        private long _activeRequests;
        private long _requestsTotal;
        private long _errorsTotal;
        private long _throttleWaitMsTotal;
        private long _latencyMsTotal;
        private string? _lastError;
        private DateTimeOffset? _lastRequestAt;

        public ProviderActivityCounter(string providerName)
        {
            ProviderName = providerName;
        }

        private string ProviderName { get; }

        public void RecordStarted(TimeSpan throttleWait)
        {
            var now = DateTimeOffset.UtcNow;
            Interlocked.Increment(ref _activeRequests);
            Interlocked.Increment(ref _requestsTotal);
            Interlocked.Add(ref _throttleWaitMsTotal, (long)throttleWait.TotalMilliseconds);
            _lastRequestAt = now;
            _requestStarts.Enqueue(now);
        }

        public void RecordCompleted(TimeSpan latency, Exception? error)
        {
            Interlocked.Decrement(ref _activeRequests);
            Interlocked.Add(ref _latencyMsTotal, (long)latency.TotalMilliseconds);

            if (error is null)
                return;

            Interlocked.Increment(ref _errorsTotal);
            _lastError = $"{error.GetType().Name}: {error.Message}";
            _errorStarts.Enqueue(DateTimeOffset.UtcNow);
        }

        public ProviderActivitySnapshot Snapshot(DateTimeOffset now)
        {
            Trim(_requestStarts, now);
            Trim(_errorStarts, now);

            var totalRequests = Interlocked.Read(ref _requestsTotal);
            var totalLatency = Interlocked.Read(ref _latencyMsTotal);

            return new ProviderActivitySnapshot(
                ProviderName,
                (int)Math.Max(0, Interlocked.Read(ref _activeRequests)),
                totalRequests,
                _requestStarts.Count,
                Interlocked.Read(ref _errorsTotal),
                _errorStarts.Count,
                Interlocked.Read(ref _throttleWaitMsTotal),
                totalRequests > 0 ? (double)totalLatency / totalRequests : 0,
                _lastRequestAt,
                _lastError);
        }

        private static void Trim(ConcurrentQueue<DateTimeOffset> queue, DateTimeOffset now)
        {
            var cutoff = now.AddMinutes(-1);
            while (queue.TryPeek(out var value) && value < cutoff)
                queue.TryDequeue(out _);
        }
    }
}

public static class ProviderRateLimitDefaults
{
    public static readonly ProviderRateLimitConfiguration Apple = new()
    {
        RequestsPerMinute = 20,
        Burst = 5,
        MaxConcurrency = 2
    };

    public static readonly ProviderRateLimitConfiguration Tmdb = new()
    {
        RequestsPerSecond = 20,
        Burst = 20,
        MaxConcurrency = 8
    };
}
