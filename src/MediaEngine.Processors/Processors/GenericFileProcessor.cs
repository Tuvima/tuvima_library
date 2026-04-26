using MediaEngine.Domain.Enums;
using MediaEngine.Processors.Contracts;
using MediaEngine.Processors.Models;

namespace MediaEngine.Processors.Processors;

/// <summary>
/// Catch-all fallback processor that handles any file format not claimed by a
/// more specific processor.
///
/// ──────────────────────────────────────────────────────────────────
/// Dispatch contract (spec: Phase 5 – Format Fallback)
/// ──────────────────────────────────────────────────────────────────
///  • <see cref="Priority"/> = <see cref="int.MinValue"/> — always loses to any
///    format-specific processor.
///  • <see cref="CanProcess"/> always returns <see langword="true"/>; it is never
///    called in practice because <see cref="MediaProcessorRouter"/> bypasses the
///    <c>CanProcess</c> check for processors registered at <c>int.MinValue</c>.
///
/// ──────────────────────────────────────────────────────────────────
/// Metadata produced (spec: Phase 5 – Metadata Extraction)
/// ──────────────────────────────────────────────────────────────────
///  • title (confidence 0.5) — file-name stem without extension.
///  No cover image; <see cref="MediaType.Unknown"/> as detected type.
///
/// Spec: Phase 5 – Media Processor Architecture § Generic Fallback.
/// </summary>
public sealed class GenericFileProcessor : IMediaProcessor
{
    /// <summary>
    /// File extensions claimed by format-specific processors.  A file with one
    /// of these extensions that reaches the generic fallback is corrupt or has
    /// a misleading extension — no format-specific processor could parse it.
    /// </summary>
    private static readonly HashSet<string> KnownFormatExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".epub",
        ".m4b", ".m4a", ".mp3", ".flac", ".ogg", ".wav",
        ".mp4", ".mkv", ".avi", ".webm",
        ".cbz", ".cbr",
    };

    /// <inheritdoc/>
    public MediaType SupportedType => MediaType.Unknown;

    /// <inheritdoc/>
    /// <remarks>
    /// <see cref="int.MinValue"/> ensures this processor is always outranked
    /// by any registered format-specific processor.
    /// </remarks>
    public int Priority => int.MinValue;

    /// <inheritdoc/>
    /// <remarks>Always returns <see langword="true"/>; the libraryItem bypasses this
    /// check for the fallback processor anyway.</remarks>
    public bool CanProcess(string filePath) => true;

    /// <inheritdoc/>
    public Task<ProcessorResult> ProcessAsync(string filePath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        // A file with a known media extension that no format-specific processor
        // could parse is corrupt, truncated, or has a misleading extension.
        var ext = Path.GetExtension(filePath);
        if (KnownFormatExtensions.Contains(ext))
        {
            return Task.FromResult(new ProcessorResult
            {
                FilePath      = filePath,
                DetectedType  = MediaType.Unknown,
                Claims        = [],
                IsCorrupt     = true,
                CorruptReason = $"File has a known media extension ({ext}) but no format-specific " +
                                "processor could parse it. The file may be corrupt, truncated, " +
                                "or have a misleading extension.",
            });
        }

        // Derive a best-effort title from the file name stem.
        var stem = Path.GetFileNameWithoutExtension(filePath);
        var claims = new List<ExtractedClaim>();

        if (!string.IsNullOrWhiteSpace(stem))
        {
            claims.Add(new ExtractedClaim
            {
                Key        = "title",
                Value      = stem,
                Confidence = 0.5,
            });
        }

        var result = new ProcessorResult
        {
            FilePath     = filePath,
            DetectedType = MediaType.Unknown,
            Claims       = claims,
        };

        return Task.FromResult(result);
    }
}
