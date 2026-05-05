using System.Text.Json;
using System.Text.Json.Serialization;

namespace MediaEngine.Plugins;

public sealed record PluginManifest
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("version")]
    public string Version { get; init; } = "1.0.0";

    [JsonPropertyName("minimum_tuvima_api_version")]
    public string MinimumTuvimaApiVersion { get; init; } = "1.0.0";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("entry_assembly")]
    public string? EntryAssembly { get; init; }

    [JsonPropertyName("entry_type")]
    public string? EntryType { get; init; }

    [JsonPropertyName("capabilities")]
    public IReadOnlyList<PluginCapabilityDescriptor> Capabilities { get; init; } = [];

    [JsonPropertyName("permissions")]
    public IReadOnlyList<string> Permissions { get; init; } = [];

    [JsonPropertyName("tool_requirements")]
    public IReadOnlyList<PluginToolRequirement> ToolRequirements { get; init; } = [];

    [JsonPropertyName("ai_permissions")]
    public IReadOnlyList<PluginAiPermission> AiPermissions { get; init; } = [];

    [JsonPropertyName("supported_platforms")]
    public IReadOnlyList<string> SupportedPlatforms { get; init; } = [];

    [JsonPropertyName("default_settings")]
    public Dictionary<string, JsonElement> DefaultSettings { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record PluginCapabilityDescriptor
{
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";
}

public sealed record PluginToolRequirement
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("version")]
    public string Version { get; init; } = "";

    [JsonPropertyName("executable_name")]
    public string ExecutableName { get; init; } = "";

    [JsonPropertyName("license")]
    public string? License { get; init; }

    [JsonPropertyName("source_url")]
    public string? SourceUrl { get; init; }

    [JsonPropertyName("platforms")]
    public IReadOnlyList<PluginToolPlatform> Platforms { get; init; } = [];
}

public sealed record PluginToolPlatform
{
    [JsonPropertyName("rid")]
    public string Rid { get; init; } = "";

    [JsonPropertyName("download_url")]
    public string? DownloadUrl { get; init; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; init; }

    [JsonPropertyName("relative_executable_path")]
    public string? RelativeExecutablePath { get; init; }
}

public sealed record PluginAiPermission
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = "";

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; init; }

    [JsonPropertyName("schedule")]
    public string Schedule { get; init; } = "scheduled";

    [JsonPropertyName("resource_class")]
    public string ResourceClass { get; init; } = "ai";
}
