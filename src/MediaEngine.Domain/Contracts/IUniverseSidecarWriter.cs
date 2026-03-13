using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Writes and reads <c>universe.xml</c> sidecar files under
/// <c>{LibraryRoot}/.universe/{NarrativeRoot}/</c>.
/// </summary>
public interface IUniverseSidecarWriter
{
    /// <summary>
    /// Write a full universe graph snapshot to <c>universe.xml</c>.
    /// </summary>
    /// <param name="universeFolderPath">
    /// Absolute path to the universe folder
    /// (e.g. <c>{LibraryRoot}/.universe/Dune (Q3041974)/</c>).
    /// </param>
    /// <param name="snapshot">The complete graph snapshot to write.</param>
    Task WriteUniverseXmlAsync(
        string universeFolderPath,
        UniverseSnapshot snapshot,
        CancellationToken ct = default);

    /// <summary>
    /// Read a <c>universe.xml</c> file back into an <see cref="UniverseSnapshot"/>.
    /// Returns <c>null</c> if the file does not exist or cannot be parsed.
    /// </summary>
    Task<UniverseSnapshot?> ReadUniverseXmlAsync(
        string xmlPath,
        CancellationToken ct = default);
}
