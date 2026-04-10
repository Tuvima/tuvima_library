using MediaEngine.Ingestion.Models;

namespace MediaEngine.Ingestion.Contracts;

/// <summary>
/// Resolves an absolute file path to the <see cref="LibraryFolderEntry"/>
/// it belongs to. Used by the ingestion pipeline so that a file arriving
/// from any of a library's configured source paths is attributed to the
/// same logical library.
///
/// A single library can declare multiple source paths
/// (e.g. <c>D:\Movies</c> and <c>E:\Movies</c> as one Movies library).
/// The resolver does longest-prefix matching across <i>all</i> source paths
/// of <i>all</i> registered libraries.
///
/// Spec: side-by-side-with-Plex plan §F.
/// </summary>
public interface ILibraryFolderResolver
{
    /// <summary>
    /// Returns the library entry whose source paths cover the given absolute
    /// file path, or <see langword="null"/> if no library claims the path.
    /// When two libraries declare overlapping prefixes, the longest match wins;
    /// the loader rejects exact-overlap configurations at startup so this
    /// case is rare in practice.
    /// </summary>
    /// <param name="absolutePath">An absolute file or directory path.</param>
    LibraryFolderEntry? ResolveForPath(string absolutePath);

    /// <summary>
    /// Returns the source path under which <paramref name="absolutePath"/>
    /// lives, or <see langword="null"/> if no library claims the path.
    /// Useful for callers that need the in-place organise root for the file
    /// (files always organise within the source path they already live in,
    /// never across source paths — see plan §F).
    /// </summary>
    string? ResolveSourcePath(string absolutePath);
}
