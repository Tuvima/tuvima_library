namespace MediaEngine.Ingestion.Models;

/// <summary>
/// Tuning parameters for <see cref="DebounceQueue"/>.
/// All defaults are designed for a standard local-disk library.
/// Reduce <see cref="SettleDelay"/> for SSDs; increase for network-attached storage.
/// </summary>
public sealed class DebounceOptions
{
    /// <summary>
    /// How long to wait after the <em>last</em> OS event for a given path before
    /// attempting the file-lock probe.
    /// Any new event for the same path resets this clock.
    /// Default: 2 seconds.
    /// </summary>
    public TimeSpan SettleDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Base interval for the first probe retry.
    /// Subsequent retries follow an exponential schedule:
    /// <c>ProbeInterval × 2^(attempt-1)</c>, capped at <see cref="MaxProbeDelay"/>.
    /// Default: 500 ms.
    /// </summary>
    public TimeSpan ProbeInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Maximum number of probe attempts before the candidate is emitted as
    /// <see cref="IngestionCandidate.IsFailed"/>.
    /// With the defaults this gives a maximum total probe window of ~127 seconds.
    /// Default: 8.
    /// </summary>
    public int MaxProbeAttempts { get; set; } = 8;

    /// <summary>
    /// Upper bound applied to the exponential backoff delay for any single attempt.
    /// Prevents a single slow file from monopolising the settle loop.
    /// Default: 30 seconds.
    /// </summary>
    public TimeSpan MaxProbeDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Capacity of the bounded output channel.
    /// If the channel is full the debounce loop will back-pressure until a
    /// consumer reads a candidate.
    /// Default: 512.
    /// </summary>
    public int QueueCapacity { get; set; } = 512;
}
