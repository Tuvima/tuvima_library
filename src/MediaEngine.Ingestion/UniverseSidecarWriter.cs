using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Models;

namespace MediaEngine.Ingestion;

/// <summary>
/// No-op implementation of <see cref="IUniverseSidecarWriter"/>.
/// Universe data is stored exclusively in the database. Sidecar files have been
/// removed — the database is the authoritative data store.
/// WriteUniverseXmlAsync does nothing; ReadUniverseXmlAsync always returns null
/// (no sidecar files are written so none will be found).
/// </summary>
public sealed class UniverseSidecarWriter : IUniverseSidecarWriter
{
    /// <inheritdoc/>
    public Task WriteUniverseXmlAsync(
        string universeFolderPath,
        UniverseSnapshot snapshot,
        CancellationToken ct = default)
        => Task.CompletedTask;

    /// <inheritdoc/>
    public Task<UniverseSnapshot?> ReadUniverseXmlAsync(
        string xmlPath,
        CancellationToken ct = default)
        => Task.FromResult<UniverseSnapshot?>(null);
}
