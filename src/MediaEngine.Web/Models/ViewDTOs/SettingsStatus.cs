using MudBlazor;

namespace MediaEngine.Web.Models.ViewDTOs;

public enum SettingsStatusKind
{
    Live,
    Partial,
    Planned,
    Experimental,
    NotConnected,
    EngineUnavailable,
    ReadOnly,
    RequiresRestart,
    RequiresProviderCredentials,
    RequiresAdminRole,
}

public static class SettingsStatus
{
    public static string Label(SettingsStatusKind status) => status switch
    {
        SettingsStatusKind.Live => "Live",
        SettingsStatusKind.Partial => "Partial",
        SettingsStatusKind.Planned => "Planned",
        SettingsStatusKind.Experimental => "Experimental",
        SettingsStatusKind.NotConnected => "Not connected",
        SettingsStatusKind.EngineUnavailable => "Engine unavailable",
        SettingsStatusKind.ReadOnly => "Read-only",
        SettingsStatusKind.RequiresRestart => "Requires restart",
        SettingsStatusKind.RequiresProviderCredentials => "Requires credentials",
        SettingsStatusKind.RequiresAdminRole => "Admin only",
        _ => status.ToString(),
    };

    public static Color Color(SettingsStatusKind status) => status switch
    {
        SettingsStatusKind.Live => MudBlazor.Color.Success,
        SettingsStatusKind.Partial => MudBlazor.Color.Info,
        SettingsStatusKind.Planned => MudBlazor.Color.Default,
        SettingsStatusKind.Experimental => MudBlazor.Color.Secondary,
        SettingsStatusKind.NotConnected => MudBlazor.Color.Warning,
        SettingsStatusKind.EngineUnavailable => MudBlazor.Color.Error,
        SettingsStatusKind.ReadOnly => MudBlazor.Color.Default,
        SettingsStatusKind.RequiresRestart => MudBlazor.Color.Warning,
        SettingsStatusKind.RequiresProviderCredentials => MudBlazor.Color.Warning,
        SettingsStatusKind.RequiresAdminRole => MudBlazor.Color.Error,
        _ => MudBlazor.Color.Default,
    };
}
