using MediaEngine.Ingestion.Models;

namespace MediaEngine.Ingestion.Contracts;

/// <summary>
/// Recursively scans a Library Root directory for <c>library.xml</c> sidecar files
/// and uses them to hydrate (or restore) the database — the "Great Inhale".
///
/// <para>
/// Design invariant: XML always wins. When the sidecar and the database disagree,
/// the sidecar value is applied. This makes the filesystem the authoritative
/// source of truth and allows full database reconstruction after a data wipe.
/// </para>
///
/// <para>
/// No file hashing or metadata extraction is performed — the scan reads XML only,
/// making it orders of magnitude faster than a full ingestion pass.
/// </para>
/// </summary>
public interface ILibraryScanner
{
    /// <summary>
    /// Scans <paramref name="libraryRoot"/> recursively, processes all
    /// <c>library.xml</c> files found, and returns a summary of the hydration.
    /// </summary>
    /// <param name="libraryRoot">Absolute path to the Library Root directory.</param>
    /// <param name="ct">Cancellation token; checked between files.</param>
    Task<LibraryScanResult> ScanAsync(
        string libraryRoot,
        CancellationToken ct = default);

    /// <summary>
    /// Scans <c>{libraryRoot}/.universe/*/universe.xml</c> files and upserts
    /// narrative roots, fictional entities, entity relationships, and work links
    /// from the XML sidecar snapshots.
    ///
    /// Called as part of the Great Inhale to recover the universe graph from
    /// the filesystem after a database wipe.
    /// </summary>
    /// <param name="libraryRoot">Absolute path to the Library Root directory.</param>
    /// <param name="ct">Cancellation token; checked between files.</param>
    /// <returns>Counts of universes, entities, and relationships restored.</returns>
    Task<UniverseScanResult> ScanUniversesAsync(
        string libraryRoot,
        CancellationToken ct = default);

    /// <summary>
    /// Scans <c>{libraryRoot}/.people/*/person.xml</c> files and upserts
    /// Person records by Wikidata QID (or by person ID if no QID). Known-names
    /// are matched against existing canonical author values to rebuild
    /// <c>person_media_links</c>.
    ///
    /// Called as part of the Great Inhale to recover person data from the
    /// filesystem after a database wipe.
    /// </summary>
    /// <param name="libraryRoot">Absolute path to the Library Root directory.</param>
    /// <param name="ct">Cancellation token; checked between files.</param>
    /// <returns>Number of person records upserted.</returns>
    Task<int> ScanPeopleAsync(
        string libraryRoot,
        CancellationToken ct = default);
}
