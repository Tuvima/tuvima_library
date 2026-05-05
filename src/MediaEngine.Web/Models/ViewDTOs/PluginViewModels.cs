using System.Text.Json;
using System.Text.Json.Serialization;

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
    public List<PluginCapabilityViewModel> Capabilities { get; set; } = [];

    [JsonPropertyName("tool_requirements")]
    public List<PluginToolRequirementViewModel> ToolRequirements { get; set; } = [];

    [JsonPropertyName("ai_permissions")]
    public List<PluginAiPermissionViewModel> AiPermissions { get; set; } = [];

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
    public List<PluginHealthCheckViewModel> Checks { get; set; } = [];
}

public sealed class PluginCapabilityViewModel
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";
}

public sealed class PluginToolRequirementViewModel
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("executable_name")]
    public string ExecutableName { get; set; } = "";

    [JsonPropertyName("license")]
    public string? License { get; set; }

    [JsonPropertyName("source_url")]
    public string? SourceUrl { get; set; }
}

public sealed class PluginAiPermissionViewModel
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; }

    [JsonPropertyName("schedule")]
    public string Schedule { get; set; } = "";

    [JsonPropertyName("resource_class")]
    public string ResourceClass { get; set; } = "";
}

public sealed class PluginHealthCheckViewModel
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "unknown";

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = [];
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
