using MediaEngine.Ingestion.Models;

namespace MediaEngine.Ingestion.Contracts;

/// <summary>
/// Scans a Library Root directory for media files and rebuilds the database
/// from embedded file metadata and batch reconciliation — the "Great Inhale v2".
///
/// <para>
/// Files already known to the database (matched by content hash) have their
/// paths updated if they have moved on disk. Files not yet in the database are
/// flagged for a follow-up ingestion pass to run processors, scoring, and
/// metadata hydration.
/// </para>
///
/// <para>
/// Person and universe data is recovered separately from <c>.people/*/person.xml</c>
/// and <c>.universe/*/universe.xml</c> sidecar files which are maintained
/// independently of the media file scanning pipeline.
/// </para>
/// </summary>
public interface ILibraryScanner
{
    /// <summary>
    /// Scans <paramref name="libraryRoot"/> for media files and returns a
    /// summary of the scan results.
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
