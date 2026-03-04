namespace MediaEngine.Domain.Entities;

/// <summary>
/// A single physical file that is part of a multi-file <see cref="MediaManifest"/>.
///
/// Examples: one disc of a multi-disc movie rip; one part of a split audiobook.
/// Spec: "Audiobooks or multi-disc movies MUST be treated as a single MediaAsset
/// via a MediaManifest to ensure continuous tracking."
/// </summary>
public sealed class MediaManifestEntry
{
    /// <summary>Stable row identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>FK → <see cref="MediaManifest.Id"/>.</summary>
    public Guid ManifestId { get; set; }

    /// <summary>
    /// Absolute or root-relative path to this specific file on the local file system.
    /// Must be within the <c>DataRoot</c> declared in <c>LegacyManifest</c>.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 (or equivalent) hash of this individual file.
    /// Used for per-part integrity verification.
    /// </summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>
    /// Zero-based playback/reading order within the manifest.
    /// The application layer MUST order entries by this value before presentation.
    /// </summary>
    public int SortIndex { get; set; }
}
