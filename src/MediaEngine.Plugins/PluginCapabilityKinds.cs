namespace MediaEngine.Plugins;

/// <summary>
/// Well-known <see cref="IPluginCapability.Kind"/> string constants shared across plugins.
/// No Api code currently filters capabilities by <see cref="IPluginCapability.Kind"/> value —
/// this class pins the contract now, before anything does, so plugins stop inventing their
/// own ad-hoc spellings.
/// </summary>
public static class PluginCapabilityKinds
{
    /// <summary>
    /// Kind reported by an <see cref="IPluginHealthCheck"/> capability. This was already the
    /// majority spelling (<c>CommercialSkipHealthCheck</c>, <c>FandomLoreHealthCheck</c>); the
    /// lone divergent spelling <c>"health-check"</c> (previously used by
    /// <c>MediaEngine.Plugin.MediaSegments</c>'s health checks) has been switched to this
    /// constant as a deliberate contract fix.
    /// </summary>
    public const string HealthCheckKind = "plugin-health-check";
}
