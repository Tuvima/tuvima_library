using System.Text.Json;
using MediaEngine.Plugins;

namespace MediaEngine.Api.Services.Plugins;

public sealed class PluginExecutionContext : IPluginExecutionContext
{
    public PluginExecutionContext(
        string pluginId,
        IReadOnlyDictionary<string, JsonElement> settings,
        string tempDirectory,
        IPluginToolRuntime tools,
        IPluginAiClient ai)
    {
        PluginId = pluginId;
        Settings = settings;
        TempDirectory = tempDirectory;
        Tools = tools;
        Ai = ai;
    }

    public string PluginId { get; }
    public IReadOnlyDictionary<string, JsonElement> Settings { get; }
    public string TempDirectory { get; }
    public IPluginToolRuntime Tools { get; }
    public IPluginAiClient Ai { get; }
}
