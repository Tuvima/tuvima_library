using MediaEngine.Plugins;

namespace MediaEngine.Plugin.MediaSegments;

public sealed class AiVisualVerifierHealthCheck : IPluginHealthCheck
{
    public string Kind => PluginCapabilityKinds.HealthCheckKind;

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
