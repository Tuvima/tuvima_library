using System.Text.Json;
using System.Text.Json.Serialization;
using MediaEngine.Plugins;

namespace MediaEngine.Web.Models.ViewDTOs;

public sealed class PluginViewModel
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("is_built_in")]
    public bool IsBuiltIn { get; set; }

    [JsonPropertyName("load_error")]
    public string? LoadError { get; set; }

    [JsonPropertyName("capabilities")]
    public List<PluginCapabilityDescriptor> Capabilities { get; set; } = [];

    [JsonPropertyName("tool_requirements")]
    public List<PluginToolRequirement> ToolRequirements { get; set; } = [];

    [JsonPropertyName("ai_permissions")]
    public List<PluginAiPermission> AiPermissions { get; set; } = [];

    [JsonPropertyName("settings")]
    public Dictionary<string, JsonElement> Settings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class PluginHealthViewModel
{
    [JsonPropertyName("plugin_id")]
    public string PluginId { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "unknown";

    [JsonPropertyName("checks")]
    public List<PluginHealthResult> Checks { get; set; } = [];
}

public sealed class PluginJobViewModel
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("pluginId")]
    public string PluginId { get; set; } = "";

    [JsonPropertyName("jobType")]
    public string JobType { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("startedAt")]
    public DateTimeOffset StartedAt { get; set; }

    [JsonPropertyName("completedAt")]
    public DateTimeOffset? CompletedAt { get; set; }

    [JsonPropertyName("assetsScanned")]
    public int AssetsScanned { get; set; }

    [JsonPropertyName("segmentsWritten")]
    public int SegmentsWritten { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
