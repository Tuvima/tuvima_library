using MediaEngine.Plugins;

namespace MediaEngine.Plugin.CommercialSkip;

public sealed class CommercialSkipHealthCheck : IPluginHealthCheck
{
    private readonly PluginManifest _manifest;

    public CommercialSkipHealthCheck(PluginManifest manifest)
    {
        _manifest = manifest;
    }

    public string Kind => "plugin-health-check";

    public async Task<PluginHealthResult> GetHealthAsync(
        IPluginExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        foreach (var requirement in _manifest.ToolRequirements)
        {
            var result = await context.Tools.ResolveToolAsync(_manifest.Id, requirement, context.Settings, cancellationToken).ConfigureAwait(false);
            if (!result.IsAvailable)
                warnings.Add($"{requirement.Id}: {result.Message ?? result.Status}");
        }

        return warnings.Count == 0
            ? new PluginHealthResult { Status = "healthy", Message = "Comskip/FFmpeg tools are available." }
            : new PluginHealthResult { Status = "degraded", Message = "One or more commercial detection tools are unavailable.", Warnings = warnings };
    }
}
