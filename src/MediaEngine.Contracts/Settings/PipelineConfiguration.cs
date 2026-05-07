using System.Text.Json.Serialization;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Contracts.Settings;

public sealed class PipelineProviderEntry
{
    [JsonPropertyName("rank")]
    public int Rank { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public sealed class MediaTypePipeline
{
    [JsonPropertyName("strategy")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ProviderStrategy Strategy { get; set; } = ProviderStrategy.Waterfall;

    [JsonPropertyName("providers")]
    public List<PipelineProviderEntry> Providers { get; set; } = [];

    [JsonPropertyName("field_priorities")]
    public Dictionary<string, List<string>> FieldPriorities { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class PipelineConfiguration
{
    [JsonPropertyName("pipelines")]
    public Dictionary<string, MediaTypePipeline> Pipelines { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public MediaTypePipeline GetPipelineForMediaType(string mediaTypeDisplayName)
    {
        return Pipelines.TryGetValue(mediaTypeDisplayName, out var pipeline) ? pipeline : new();
    }
}
