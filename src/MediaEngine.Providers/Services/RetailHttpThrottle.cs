namespace MediaEngine.Providers.Services;

public sealed class RetailHttpThrottle
{
    private readonly SemaphoreSlim _itunesThrottle = new(1, 1);
    private readonly SemaphoreSlim _tmdbThrottle = new(1, 1);
    private DateTime _itunesLastCallUtc = DateTime.MinValue;
    private DateTime _tmdbLastCallUtc = DateTime.MinValue;

    public Task ThrottleItunesAsync(CancellationToken ct)
        => ThrottleAsync(_itunesThrottle, () => _itunesLastCallUtc, value => _itunesLastCallUtc = value, 300, ct);

    public Task ThrottleTmdbAsync(CancellationToken ct)
        => ThrottleAsync(_tmdbThrottle, () => _tmdbLastCallUtc, value => _tmdbLastCallUtc = value, 250, ct);

    private static async Task ThrottleAsync(
        SemaphoreSlim gate,
        Func<DateTime> getLastCallUtc,
        Action<DateTime> setLastCallUtc,
        int throttleMs,
        CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var elapsed = (DateTime.UtcNow - getLastCallUtc()).TotalMilliseconds;
            if (elapsed < throttleMs)
                await Task.Delay(TimeSpan.FromMilliseconds(throttleMs - elapsed), ct)
                    .ConfigureAwait(false);

            setLastCallUtc(DateTime.UtcNow);
        }
        finally
        {
            gate.Release();
        }
    }
}
