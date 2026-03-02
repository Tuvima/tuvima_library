using Tanaste.Web.Models.ViewDTOs;
using Tanaste.Web.Services.Integration;

namespace Tanaste.Web.Services.Theming;

/// <summary>
/// Per-circuit (scoped) service that holds the active device class and the
/// fully-resolved UI settings for this browser session.
///
/// <para>
/// Replaces the former AutomotiveModeService with a generalised device-context
/// model. Each Blazor Server circuit maintains its own device class and resolved
/// settings so that a TV in television mode does not affect a phone in mobile mode.
/// </para>
///
/// <para>
/// Initialised by <c>MainLayout.OnAfterRenderAsync</c> via JS interop — the browser
/// calls <c>detectDeviceClass()</c> to determine the active device class (from URL
/// param, localStorage, or auto-detection), then the service fetches resolved settings
/// from the Engine API.
/// </para>
///
/// <para>
/// Components subscribe to <see cref="OnChanged"/> and call <c>StateHasChanged()</c>
/// in the handler. They must unsubscribe in <c>Dispose()</c> to prevent memory leaks.
/// </para>
/// </summary>
public sealed class DeviceContextService
{
    private readonly ITanasteApiClient _apiClient;

    public DeviceContextService(ITanasteApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    /// <summary>The active device class for this circuit (web, mobile, television, automotive).</summary>
    public string DeviceClass { get; private set; } = "web";

    /// <summary>
    /// The fully-resolved UI settings for the current device class and profile.
    /// Falls back to compiled defaults if the Engine is unreachable.
    /// </summary>
    public ResolvedUISettingsViewModel Settings { get; private set; } = new();

    /// <summary>Whether the service has been initialised with a device class and resolved settings.</summary>
    public bool IsInitialised { get; private set; }

    /// <summary>
    /// Fires whenever the device class or resolved settings change.
    /// Subscribe in <c>OnInitialized</c> and unsubscribe in <c>Dispose()</c>.
    /// </summary>
    public event Action? OnChanged;

    /// <summary>
    /// Initialises the service with the detected device class and fetches resolved settings
    /// from the Engine API. Called once per circuit from <c>MainLayout.OnAfterRenderAsync</c>.
    /// </summary>
    /// <param name="deviceClass">Device class detected by JS interop.</param>
    /// <param name="profileId">Optional profile UUID for the current user.</param>
    public async Task InitialiseAsync(string deviceClass, string? profileId = null)
    {
        DeviceClass = string.IsNullOrWhiteSpace(deviceClass) ? "web" : deviceClass;

        var resolved = await _apiClient.GetResolvedUISettingsAsync(DeviceClass, profileId);
        Settings = resolved ?? new ResolvedUISettingsViewModel { DeviceClass = DeviceClass };

        IsInitialised = true;
        OnChanged?.Invoke();
    }

    /// <summary>
    /// Switches the device class at runtime (e.g. from a device-selector UI)
    /// and re-fetches resolved settings from the Engine API.
    /// </summary>
    public async Task SwitchDeviceAsync(string deviceClass, string? profileId = null)
    {
        DeviceClass = string.IsNullOrWhiteSpace(deviceClass) ? "web" : deviceClass;

        var resolved = await _apiClient.GetResolvedUISettingsAsync(DeviceClass, profileId);
        Settings = resolved ?? new ResolvedUISettingsViewModel { DeviceClass = DeviceClass };

        OnChanged?.Invoke();
    }

    // ── Convenience accessors ──────────────────────────────────────────────

    /// <summary>Shorthand: returns <c>true</c> if the named feature is disabled.</summary>
    public bool IsFeatureDisabled(string feature) => Settings.IsFeatureDisabled(feature);

    /// <summary>Shorthand: returns <c>true</c> if the named page is disabled.</summary>
    public bool IsPageDisabled(string page) => Settings.IsPageDisabled(page);

    /// <summary>Whether the device is automotive.</summary>
    public bool IsAutomotive => string.Equals(DeviceClass, "automotive", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the device is television.</summary>
    public bool IsTelevision => string.Equals(DeviceClass, "television", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the device is mobile.</summary>
    public bool IsMobile => string.Equals(DeviceClass, "mobile", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the device is the standard web (desktop) class.</summary>
    public bool IsWeb => string.Equals(DeviceClass, "web", StringComparison.OrdinalIgnoreCase);
}
