using MediaEngine.Processors.Models;

namespace MediaEngine.Processors.Contracts;

/// <summary>
/// Owns the ordered collection of <see cref="IMediaProcessor"/> instances and
/// dispatches files to the correct processor.
///
/// ──────────────────────────────────────────────────────────────────
/// Dispatch rules (spec: Phase 5 – Format Fallback)
/// ──────────────────────────────────────────────────────────────────
///  1. All registered processors have their <see cref="IMediaProcessor.CanProcess"/>
///     called; the one with the highest <see cref="IMediaProcessor.Priority"/>
///     that returns <see langword="true"/> is selected.
///  2. If no high-fidelity processor matches, the generic fallback
///     (priority = <see cref="int.MinValue"/>) is used unconditionally.
///  3. Concurrent <see cref="ProcessAsync"/> calls are throttled by an internal
///     <see cref="System.Threading.SemaphoreSlim"/> to prevent runaway memory usage
///     when large batches of files are ingested simultaneously.
///
/// Spec: Phase 5 – Media Processor Architecture § Processor Registry.
/// </summary>
public interface IProcessorRegistry
{
    /// <summary>
    /// Adds <paramref name="processor"/> to the registry.
    /// Duplicate <see cref="IMediaProcessor.SupportedType"/> + <see cref="IMediaProcessor.Priority"/>
    /// combinations are allowed; the highest-priority one wins dispatch.
    /// </summary>
    void Register(IMediaProcessor processor);

    /// <summary>
    /// Returns the best-match processor for <paramref name="filePath"/> by running
    /// <see cref="IMediaProcessor.CanProcess"/> on each registered processor (sorted
    /// by descending priority) and returning the first match.
    /// Falls back to the generic processor when no specific match is found.
    /// </summary>
    /// <returns>
    /// The selected <see cref="IMediaProcessor"/>, or <see langword="null"/> if
    /// no processor (including the fallback) is registered.
    /// </returns>
    IMediaProcessor? Resolve(string filePath);

    /// <summary>
    /// Resolves the correct processor for <paramref name="filePath"/> and runs
    /// <see cref="IMediaProcessor.ProcessAsync"/>, throttled by the registry's
    /// internal semaphore.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no processor (including the generic fallback) is registered.
    /// </exception>
    Task<ProcessorResult> ProcessAsync(string filePath, CancellationToken ct = default);
}
