namespace MediaEngine.Contracts.Plugins;

/// <summary>
/// Response for <c>POST /plugins/{pluginId}/enable</c> and <c>/disable</c>. Property names
/// (including snake_case spelling) are byte-identical to the anonymous object this record
/// replaced (Stage 5A wave 2 response-shape promotion) so the wire shape does not change.
/// </summary>
public sealed record PluginEnabledResponse
{
    public string plugin_id { get; init; } = string.Empty;
    public bool enabled { get; init; }
}

/// <summary>
/// Response for <c>PUT /plugins/{pluginId}/settings</c> and <c>PUT /plugins/{pluginId}/manifest</c>.
/// </summary>
public sealed record PluginSavedResponse
{
    public string plugin_id { get; init; } = string.Empty;
    public bool saved { get; init; }
}

/// <summary>
/// Response for <c>GET /plugins/{pluginId}/manifest</c>.
/// </summary>
public sealed record PluginManifestJsonResponse
{
    public string plugin_id { get; init; } = string.Empty;
    public string json { get; init; } = string.Empty;
}

/// <summary>
/// Response for <c>DELETE /plugins/{pluginId}</c>.
/// </summary>
public sealed record PluginDeletedResponse
{
    public string plugin_id { get; init; } = string.Empty;
    public bool deleted { get; init; }
}
