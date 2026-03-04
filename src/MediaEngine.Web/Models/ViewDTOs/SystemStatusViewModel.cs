namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>Result of GET /system/status — used by the status indicator in the nav bar.</summary>
public sealed class SystemStatusViewModel
{
    public string Status  { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;

    public bool IsHealthy => Status.Equals("ok", StringComparison.OrdinalIgnoreCase);
}
