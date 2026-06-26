using MediaEngine.Plugins;

namespace MediaEngine.Plugin.FandomLore;

public sealed class FandomLoreHealthCheck : IPluginHealthCheck
{
    public string Kind => "plugin-health-check";

    public Task<PluginHealthResult> GetHealthAsync(
        IPluginExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var contentMode = FandomSettings.ReadString(context.Settings, "content_mode", "structured_only");
        var status = string.Equals(contentMode, "structured_only", StringComparison.OrdinalIgnoreCase)
            ? "healthy"
            : "degraded";

        var message = status == "healthy"
            ? "Fandom lore is configured for structured-only extraction."
            : "Fandom lore only supports structured-only extraction in this version.";

        return Task.FromResult(new PluginHealthResult
        {
            Status = status,
            Message = message,
        });
    }
}
