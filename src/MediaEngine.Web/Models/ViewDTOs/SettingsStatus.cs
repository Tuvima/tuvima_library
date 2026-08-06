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
