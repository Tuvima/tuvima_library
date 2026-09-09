using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.Plugins;

public sealed record PluginApplicationOperationDto(
    [property: JsonPropertyName("operation_id")] string OperationId,
    [property: JsonPropertyName("plugin_id")] string PluginId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("permission_id")] string PermissionId,
    [property: JsonPropertyName("request_contract")] string RequestContract,
    [property: JsonPropertyName("response_contract")] string ResponseContract,
    [property: JsonPropertyName("is_available")] bool IsAvailable,
    [property: JsonPropertyName("unavailable_reason")] string? UnavailableReason);

public sealed record DiscoverUniverseLoreSourcesRequest(
    [property: JsonPropertyName("universe_qid")] string UniverseQid);

public sealed record DiscoverUniverseLoreSourcesResponse(
    [property: JsonPropertyName("universe_qid")] string UniverseQid,
    [property: JsonPropertyName("sources")] IReadOnlyList<PluginLoreSourceCandidateDto> Sources);

public sealed record PluginLoreSourceCandidateDto(
    [property: JsonPropertyName("source_key")] string SourceKey,
    [property: JsonPropertyName("source_name")] string SourceName,
    [property: JsonPropertyName("base_url")] string BaseUrl,
    [property: JsonPropertyName("api_url")] string ApiUrl,
    [property: JsonPropertyName("license")] string? License,
    [property: JsonPropertyName("confidence")] double Confidence);
