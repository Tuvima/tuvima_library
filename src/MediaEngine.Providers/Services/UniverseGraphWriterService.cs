using MediaEngine.Domain.Contracts;

namespace MediaEngine.Providers.Services;

/// <summary>
/// No-op implementation of <see cref="IUniverseGraphWriterService"/>.
/// Universe data is stored exclusively in the database (fictional_entities,
/// entity_relationships, narrative_roots tables). Sidecar files have been
/// removed — the database is the authoritative data store.
/// </summary>
public sealed class UniverseGraphWriterService : IUniverseGraphWriterService
{
    /// <inheritdoc/>
    public Task NotifyEntityEnrichedAsync(string universeQid, CancellationToken ct = default)
        => Task.CompletedTask;
}
