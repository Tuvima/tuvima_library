using MediaEngine.Ingestion.Models;

namespace MediaEngine.Ingestion.Contracts;

/// <summary>
/// Reads and writes sidecar XML files for editions and persons.
/// <list type="bullet">
///   <item>Edition level — one <c>library.xml</c> per Edition folder, records file identity, locks, and cover path.</item>
///   <item>Person level — one <c>person.xml</c> per person folder, records identity and social links.</item>
/// </list>
///
/// Hub-level sidecars have been dropped — edition + universe + person sidecars
/// contain all recoverable data.  Hubs are reconstructed from edition sidecars
/// during Great Inhale.
///
/// The sidecar is the portable source of truth that enables Great Inhale
/// to rebuild the database from the filesystem alone after a data wipe.
///
/// Implementations MUST be thread-safe: multiple ingestion tasks may call
/// write methods concurrently for files in different folders.
/// </summary>
public interface ISidecarWriter
{
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
    /// Reads the Edition-level sidecar at <paramref name="xmlPath"/>.
    /// Returns null if the file cannot be parsed or does not contain a
    /// <c>&lt;library-edition&gt;</c> root element.
    /// </summary>
    Task<EditionSidecarData?> ReadEditionSidecarAsync(
        string xmlPath,
        CancellationToken ct = default);

    /// <summary>
    /// Writes (or overwrites) <c>person.xml</c> inside
    /// <paramref name="personFolderPath"/>.
    /// Creates the folder if it does not exist.
    /// </summary>
    Task WritePersonSidecarAsync(
        string personFolderPath,
        PersonSidecarData data,
        CancellationToken ct = default);

    /// <summary>
    /// Reads a person sidecar at <paramref name="xmlPath"/>.
    /// Returns null if the file cannot be parsed or does not contain a
    /// <c>&lt;library-person&gt;</c> root element.
    /// </summary>
    Task<PersonSidecarData?> ReadPersonSidecarAsync(
        string xmlPath,
        CancellationToken ct = default);
}
