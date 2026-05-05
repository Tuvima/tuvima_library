using MediaEngine.Plugins;

namespace MediaEngine.Plugin.MediaSegments;

public sealed class FfmpegToolHealthCheck : IPluginHealthCheck
{
    private readonly PluginManifest _manifest;
    public FfmpegToolHealthCheck(PluginManifest manifest) => _manifest = manifest;
    public string Kind => "health-check";

    public async Task<PluginHealthResult> GetHealthAsync(IPluginExecutionContext context, CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        foreach (var requirement in _manifest.ToolRequirements)
        {
            var tool = await context.Tools.ResolveToolAsync(_manifest.Id, requirement, context.Settings, cancellationToken).ConfigureAwait(false);
            if (!tool.IsAvailable)
                warnings.Add($"{requirement.Id}: {tool.Message ?? tool.Status}");
        }

        return warnings.Count == 0
            ? new PluginHealthResult { Status = "healthy", Message = "Required media tools are available." }
            : new PluginHealthResult { Status = "degraded", Message = "Some media tools are unavailable.", Warnings = warnings };
    }
}

public sealed class AiVisualVerifierHealthCheck : IPluginHealthCheck
{
    public string Kind => "health-check";

    public Task<PluginHealthResult> GetHealthAsync(IPluginExecutionContext context, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PluginHealthResult
        {
            Status = "degraded",
            Message = "Multimodal local inference is not wired yet. This plugin is a permissioned placeholder and does not run during playback.",
            Warnings = ["Gemma vision support must be validated before this verifier can classify frames."],
        });
    }
}
