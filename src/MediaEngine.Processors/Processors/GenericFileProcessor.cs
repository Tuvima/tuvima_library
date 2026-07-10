using MediaEngine.Domain.Enums;
using MediaEngine.Processors.Contracts;
using MediaEngine.Processors.Models;
using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Models;

namespace MediaEngine.Processors.Processors;

/// <summary>
/// Catch-all fallback processor that handles any file format not claimed by a
/// more specific processor.
/// </summary>
public sealed class GenericFileProcessor : IMediaProcessor
{
    private readonly IMediaTypeExtensionCatalog? _extensionCatalog;
    private readonly IReadOnlySet<string> _defaultKnownFormatExtensions;

    public GenericFileProcessor()
        : this(null)
    {
    }

    public GenericFileProcessor(IMediaTypeExtensionCatalog? extensionCatalog)
    {
        _extensionCatalog = extensionCatalog;
        _defaultKnownFormatExtensions = MediaTypeConfiguration.DefaultTypes()
            .SelectMany(type => type.Extensions)
            .Select(NormalizeExtension)
            .Where(extension => !string.IsNullOrWhiteSpace(extension))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public MediaType SupportedType => MediaType.Unknown;

    /// <inheritdoc/>
    public int Priority => int.MinValue;

    /// <inheritdoc/>
    public bool CanProcess(string filePath) => true;

    /// <inheritdoc/>
    public Task<ProcessorResult> ProcessAsync(string filePath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var ext = Path.GetExtension(filePath);
        if (IsKnownFormatExtension(ext))
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

        return Task.FromResult(new ProcessorResult
        {
            FilePath     = filePath,
            DetectedType = MediaType.Unknown,
            Claims       = claims,
        });
    }

    private bool IsKnownFormatExtension(string? extension) =>
        _extensionCatalog?.IsKnownMediaExtension(extension) == true
        || _defaultKnownFormatExtensions.Contains(NormalizeExtension(extension));

    private static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return string.Empty;

        var trimmed = extension.Trim().ToLowerInvariant();
        return trimmed.StartsWith(".", StringComparison.Ordinal) ? trimmed : "." + trimmed;
    }
}
