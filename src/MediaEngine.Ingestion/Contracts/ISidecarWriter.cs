using MediaEngine.Ingestion.Models;

namespace MediaEngine.Ingestion.Contracts;

/// <summary>
/// Reads and writes <c>library.xml</c> sidecar files at two levels:
/// <list type="bullet">
///   <item>Hub level — one file per Hub folder, records work identity.</item>
///   <item>Edition level — one file per Edition folder, records file identity, locks, and cover path.</item>
/// </list>
///
/// The sidecar is the portable source of truth that enables Great Inhale
/// to rebuild the database from the filesystem alone after a data wipe.
///
/// Implementations MUST be thread-safe: multiple ingestion tasks may call
/// write methods concurrently for files in different Hub folders.
/// </summary>
public interface ISidecarWriter
{
    /// <summary>
    /// Writes (or overwrites) <c>library.xml</c> inside <paramref name="hubFolderPath"/>.
    /// Creates the folder if it does not exist.
    /// The operation is idempotent — subsequent calls with the same path
    /// replace the previous file.
    /// </summary>
    Task WriteHubSidecarAsync(
        string         hubFolderPath,
        HubSidecarData data,
        CancellationToken ct = default);

    /// <summary>
    /// Writes (or overwrites) <c>library.xml</c> inside
    /// <paramref name="editionFolderPath"/>.
    /// Creates the folder if it does not exist.
    /// </summary>
    Task WriteEditionSidecarAsync(
        string            editionFolderPath,
        EditionSidecarData data,
        CancellationToken  ct = default);

    /// <summary>
    /// Reads the Hub-level sidecar at <paramref name="xmlPath"/>.
    /// Returns null if the file cannot be parsed or does not contain a
    /// <c>&lt;library-hub&gt;</c> root element.
    /// </summary>
    Task<HubSidecarData?> ReadHubSidecarAsync(
        string xmlPath,
        CancellationToken ct = default);

    /// <summary>
    /// Reads the Edition-level sidecar at <paramref name="xmlPath"/>.
    /// Returns null if the file cannot be parsed or does not contain a
    /// <c>&lt;library-edition&gt;</c> root element.
    /// </summary>
    Task<EditionSidecarData?> ReadEditionSidecarAsync(
        string xmlPath,
        CancellationToken ct = default);
}
