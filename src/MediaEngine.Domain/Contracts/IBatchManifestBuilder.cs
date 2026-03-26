using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Analyzes a batch of incoming files together and produces an Ingestion Manifest.
/// The manifest groups files, identifies media types, and specifies targeted retail queries.
/// </summary>
public interface IBatchManifestBuilder
{
    /// <summary>
    /// Analyze a batch of file paths and their extracted metadata to produce a manifest.
    /// Uses the text_quality model.
    /// </summary>
    /// <param name="files">File paths with any already-extracted metadata (from processors).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IngestionManifest> AnalyzeAsync(
        IReadOnlyList<BatchFileInput> files,
        CancellationToken ct = default);
}

/// <summary>
/// Input for a single file in a batch analysis request.
/// </summary>
public sealed class BatchFileInput
{
    /// <summary>Full path to the file.</summary>
    public required string FilePath { get; init; }

    /// <summary>File extension (e.g. ".epub", ".mp3").</summary>
    public required string Extension { get; init; }

    /// <summary>File size in bytes.</summary>
    public long FileSizeBytes { get; init; }

    /// <summary>Container format detected by magic bytes (e.g. "MP3", "MKV").</summary>
    public string? Container { get; init; }

    /// <summary>Duration in seconds (audio/video only).</summary>
    public double? DurationSeconds { get; init; }

    /// <summary>Any metadata already extracted by the processor (title, author, etc.).</summary>
    public IReadOnlyDictionary<string, string> ExtractedMetadata { get; init; } = new Dictionary<string, string>();
}
