using System.Text.Json;

namespace MediaEngine.Plugins;

public interface ITuvimaPlugin
{
    PluginManifest Manifest { get; }
    IReadOnlyList<IPluginCapability> CreateCapabilities();
}

public interface IPluginCapability
{
    string Kind { get; }
}

public interface IPlaybackSegmentDetector : IPluginCapability
{
    bool CanAnalyze(PluginMediaAssetContext asset);
    Task<IReadOnlyList<PluginPlaybackSegment>> AnalyzeAsync(
        PluginMediaAssetContext asset,
        IPluginExecutionContext context,
        CancellationToken cancellationToken = default);
}

public interface IPluginJob : IPluginCapability
{
    Task RunAsync(IPluginExecutionContext context, CancellationToken cancellationToken = default);
}

public interface IPluginSettingsSchemaProvider : IPluginCapability
{
    JsonElement GetSettingsSchema();
}

public interface IPluginHealthCheck : IPluginCapability
{
    Task<PluginHealthResult> GetHealthAsync(
        IPluginExecutionContext context,
        CancellationToken cancellationToken = default);
}

public interface IPluginExecutionContext
{
    string PluginId { get; }
    IReadOnlyDictionary<string, JsonElement> Settings { get; }
    string TempDirectory { get; }
    IPluginToolRuntime Tools { get; }
    IPluginAiClient Ai { get; }
}

public interface IPluginToolRuntime
{
    Task<PluginToolResolution> ResolveToolAsync(
        string pluginId,
        PluginToolRequirement requirement,
        IReadOnlyDictionary<string, JsonElement> settings,
        CancellationToken cancellationToken = default);

    Task<PluginToolRunResult> RunToolAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

public interface IPluginAiClient
{
    Task<string?> InferTextAsync(
        string pluginId,
        string role,
        string prompt,
        PluginAiOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<T?> InferJsonAsync<T>(
        string pluginId,
        string role,
        string prompt,
        string grammar,
        PluginAiOptions? options = null,
        CancellationToken cancellationToken = default) where T : class;
}
