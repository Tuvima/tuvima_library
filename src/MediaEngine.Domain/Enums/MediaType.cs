namespace MediaEngine.Domain.Enums;

/// <summary>
/// Discriminator for the kind of intellectual content a <see cref="Aggregates.Work"/> represents.
/// Maps to the <c>media_type</c> TEXT column in the <c>works</c> table.
/// New types are added here; the storage layer persists the string name (e.g. "Movie").
/// Spec: Phase 2 – Extension Points § Polymorphic Assets.
/// </summary>
public enum MediaType
{
    /// <summary>
    /// Type could not be determined; used by <c>GenericFileProcessor</c> as a fallback.
    /// Explicit value 0 ensures uninitialized fields default to Unknown rather than Movie.
    /// </summary>
    Unknown = 0,

    /// <summary>Feature-length film or short film.</summary>
    Movie,

    /// <summary>Electronic publication (EPUB / PDF).</summary>
    Epub,

    /// <summary>Narrated audio version of a book. May span multiple files via <c>MediaManifest</c>.</summary>
    Audiobook,

    /// <summary>Sequential-art publication (CBZ, CBR, PDF comic).</summary>
    Comic,

    /// <summary>Episodic television or web series.</summary>
    TvShow,

    /// <summary>Audio podcast series or individual episode.</summary>
    Podcast,

    /// <summary>Music album or single track.</summary>
    Music,
}
