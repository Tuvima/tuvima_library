namespace MediaEngine.Ingestion.Contracts;

/// <summary>
/// Manages the task queue for hashing and media processing.
/// Spec: Phase 7 – Interfaces § IBackgroundWorker.
///
/// Implementations MUST limit concurrency via a semaphore to prevent I/O
/// saturation (spec: "Resource Semaphores" scalability constraint).
/// </summary>
public interface IBackgroundWorker
{
    /// <summary>
    /// Enqueues a unit of work for execution on the background thread pool.
    /// Back-pressures the caller if the internal queue is at capacity.
    /// </summary>
    /// <param name="workItem">The payload to process.</param>
    /// <param name="handler">The processing function. Receives the payload and a cancellation token.</param>
    /// <param name="ct">Cancellation token for the enqueue operation itself.</param>
    ValueTask EnqueueAsync<T>(
        T workItem,
        Func<T, CancellationToken, Task> handler,
        CancellationToken ct = default);

    /// <summary>Number of items currently waiting in the queue.</summary>
    int PendingCount { get; }

    /// <summary>Signals that no further work will be enqueued and drains the queue.</summary>
    Task DrainAsync(CancellationToken ct = default);
}
