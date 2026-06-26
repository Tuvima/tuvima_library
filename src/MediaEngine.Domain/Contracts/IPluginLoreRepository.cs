using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Contracts;

public interface IPluginLoreRepository
{
    Task<IReadOnlyList<PluginLoreSourceRecord>> GetSourcesAsync(
        string universeQid,
        CancellationToken ct = default);

    Task<IReadOnlyList<PluginLoreSourceRecord>> GetApprovedSourcesAsync(
        string universeQid,
        CancellationToken ct = default);

    Task<PluginLoreSourceRecord?> FindSourceAsync(
        Guid sourceId,
        CancellationToken ct = default);

    Task UpsertSourceCandidateAsync(
        string universeQid,
        string pluginId,
        PluginLoreSourceCandidateRecord candidate,
        CancellationToken ct = default);

    Task<PluginLoreSourceRecord> AddManualSourceAsync(
        string universeQid,
        string pluginId,
        string sourceName,
        string baseUrl,
        string apiUrl,
        CancellationToken ct = default);

    Task SetSourceStatusAsync(
        Guid sourceId,
        string status,
        string? actor,
        CancellationToken ct = default);

    Task UpsertExtractionResultAsync(
        PluginLoreSourceRecord source,
        IReadOnlyList<PluginLoreEntityRecord> entities,
        IReadOnlyList<PluginLoreRelationshipRecord> relationships,
        CancellationToken ct = default);

    Task<IReadOnlyList<PluginLoreEntityRecord>> GetEntitiesAsync(
        string universeQid,
        bool approvedOnly = true,
        CancellationToken ct = default);

    Task<IReadOnlyList<PluginLoreRelationshipRecord>> GetRelationshipsAsync(
        string universeQid,
        bool approvedOnly = true,
        CancellationToken ct = default);
}
