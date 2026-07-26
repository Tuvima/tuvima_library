namespace MediaEngine.Plugins;

/// <summary>
/// Shared implementation of the common "do this plugin's declared tool requirements
/// resolve?" health check. Consolidates the identical loop previously duplicated by
/// <c>CommercialSkipHealthCheck</c> (MediaEngine.Plugin.CommercialSkip) and
/// <c>FfmpegToolHealthCheck</c> (MediaEngine.Plugin.MediaSegments): both walked
/// <see cref="PluginManifest.ToolRequirements"/>, resolved each via
/// <see cref="IPluginToolRuntime.ResolveToolAsync"/>, collected unavailable-tool warnings,
/// and reported healthy/degraded based on whether any warnings were produced. Only the
/// status messages differed between the two, so those are now constructor parameters.
/// </summary>
public sealed class ToolRequirementHealthCheck : IPluginHealthCheck
{
    private readonly PluginManifest _manifest;
    private readonly string _healthyMessage;
    private readonly string _degradedMessage;

    public ToolRequirementHealthCheck(PluginManifest manifest, string healthyMessage, string degradedMessage)
    {
        _manifest = manifest;
        _healthyMessage = healthyMessage;
        _degradedMessage = degradedMessage;
    }

    public string Kind => PluginCapabilityKinds.HealthCheckKind;

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
            ? new PluginHealthResult { Status = "healthy", Message = _healthyMessage }
            : new PluginHealthResult { Status = "degraded", Message = _degradedMessage, Warnings = warnings };
    }
}
