using System.Text.Json;

namespace MediaEngine.Plugins;

public interface IUniverseLoreProvider : IPluginCapability
{
    Task<IReadOnlyList<PluginLoreSourceCandidate>> DiscoverSourcesAsync(
        PluginUniverseLoreContext universe,
        IPluginExecutionContext context,
        CancellationToken cancellationToken = default);

    Task<PluginUniverseLoreResult> EnrichUniverseAsync(
        PluginUniverseLoreContext universe,
        PluginLoreSource source,
        IPluginExecutionContext context,
        CancellationToken cancellationToken = default);
}

public sealed record PluginUniverseLoreContext(
    string UniverseQid,
    string? UniverseLabel);

public sealed record PluginLoreSource(
    Guid Id,
    string UniverseQid,
    string PluginId,
    string SourceKey,
    string SourceName,
    string BaseUrl,
    string ApiUrl,
    string? License,
    double Confidence,
    JsonElement Evidence);

public sealed record PluginLoreSourceCandidate(
    string SourceKey,
    string SourceName,
    string BaseUrl,
    string ApiUrl,
    string? License,
    double Confidence,
    JsonElement Evidence);

public sealed record PluginUniverseLoreResult(
    IReadOnlyList<PluginLoreEntity> Entities,
    IReadOnlyList<PluginLoreRelationship> Relationships,
    string? Summary = null);

public sealed record PluginLoreEntity(
    string ExternalKey,
    string Label,
    string EntityType,
    string? WikidataQid,
    string? Description,
    IReadOnlyList<string> Aliases,
    string SourceUrl,
    double Confidence,
    JsonElement Evidence);

public sealed record PluginLoreRelationship(
    string SubjectExternalKey,
    string? SubjectQid,
    string ObjectExternalKey,
    string? ObjectQid,
    string RelationshipType,
    string SourceUrl,
    double Confidence,
    JsonElement Evidence);
