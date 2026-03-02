using Tanaste.Storage.Contracts;
using Tanaste.Storage.Models;

namespace Tanaste.Storage;

/// <summary>
/// Resolves the three-tier cascade (Global → Device → Profile) into a single
/// <see cref="ResolvedUISettings"/> object with concrete values for every field.
///
/// <para>
/// The resolver loads each tier from <see cref="IConfigurationLoader"/> and merges
/// them in order. Device constraints (features disabled, pages disabled, force dark
/// mode) are hard limits that Profile preferences cannot override.
/// </para>
/// </summary>
public sealed class UISettingsCascadeResolver
{
    private readonly IConfigurationLoader _configLoader;

    public UISettingsCascadeResolver(IConfigurationLoader configLoader)
    {
        _configLoader = configLoader;
    }

    /// <summary>
    /// Produces a fully-resolved UI configuration for the given device class and
    /// optional profile.
    /// </summary>
    /// <param name="deviceClass">Device class identifier (e.g. <c>"web"</c>, <c>"mobile"</c>).</param>
    /// <param name="profileId">Profile UUID. When <c>null</c>, only Global + Device are merged.</param>
    public ResolvedUISettings Resolve(string deviceClass, string? profileId = null)
    {
        // 1. Load global (always exists — falls back to compiled defaults)
        var global = _configLoader.LoadConfig<UIGlobalSettings>("ui", "global")
                     ?? new UIGlobalSettings();

        // 2. Load device profile (nullable — web is unconstrained, may not exist)
        var device = _configLoader.LoadConfig<UIDeviceProfile>("ui/devices", deviceClass);

        // 3. Load profile preferences (nullable — omitted means "use cascade defaults")
        UIProfileSettings? profile = null;
        if (!string.IsNullOrWhiteSpace(profileId))
            profile = _configLoader.LoadConfig<UIProfileSettings>("ui/profiles", profileId);

        // 4. Cascade merge
        return CascadeMerge(global, device, profile, deviceClass);
    }

    // ── Cascade logic ──────────────────────────────────────────────────────

    private static ResolvedUISettings CascadeMerge(
        UIGlobalSettings global,
        UIDeviceProfile? device,
        UIProfileSettings? profile,
        string deviceClass)
    {
        // Start with global defaults
        var result = new ResolvedUISettings
        {
            DeviceClass     = deviceClass,
            DarkMode        = global.DarkMode,
            AccentColor     = global.AccentColor,
            ContentPadding  = global.ContentPadding,
            ContentMaxWidth = global.ContentMaxWidth,
            BorderRadius    = global.BorderRadius,
            Features        = CloneFeatures(global.Features),
            Shell           = CloneShell(global.Shell),
            Pages           = ClonePages(global.Pages),
        };

        // Apply device overrides
        if (device is not null)
        {
            result.Constraints = device.Constraints ?? new UIDeviceConstraints();

            if (device.DarkMode.HasValue)
                result.DarkMode = device.DarkMode.Value;
            if (device.ContentPadding is not null)
                result.ContentPadding = device.ContentPadding;
            if (device.ContentMaxWidth is not null)
                result.ContentMaxWidth = device.ContentMaxWidth;
            if (device.BorderRadius.HasValue)
                result.BorderRadius = device.BorderRadius.Value;
            if (device.Features is not null)
                MergeFeatures(result.Features, device.Features);
            if (device.Shell is not null)
                MergeShell(result.Shell, device.Shell);
            if (device.Pages is not null)
                MergePages(result.Pages, device.Pages);
        }

        // Apply constraint: force dark mode
        if (result.Constraints.ForceDarkMode)
            result.DarkMode = true;

        // Apply constraint: disable features
        foreach (var disabled in result.Constraints.FeaturesDisabled)
        {
            SetFeatureFlag(result.Features, disabled, false);
        }

        // Apply profile overrides (respecting constraints)
        if (profile is not null)
        {
            if (profile.DarkMode.HasValue && !result.Constraints.ForceDarkMode)
                result.DarkMode = profile.DarkMode.Value;
            if (profile.AccentColor is not null)
                result.AccentColor = profile.AccentColor;
            if (profile.BorderRadius.HasValue)
                result.BorderRadius = profile.BorderRadius.Value;
        }

        return result;
    }

    // ── Clone helpers (deep copy to avoid mutating cached config objects) ───

    private static UIFeatureFlags CloneFeatures(UIFeatureFlags source) => new()
    {
        CommandPalette    = source.CommandPalette,
        SearchButton      = source.SearchButton,
        ThemeToggle       = source.ThemeToggle,
        AvatarMenu        = source.AvatarMenu,
        ServerSettings    = source.ServerSettings,
        PendingFilesAlert = source.PendingFilesAlert,
        ViewToggle        = source.ViewToggle,
        ProfileSection    = source.ProfileSection,
        ColorPicker       = source.ColorPicker,
    };

    private static UIShellSettings CloneShell(UIShellSettings source) => new()
    {
        AppBarStyle     = source.AppBarStyle,
        LogoVariant     = source.LogoVariant,
        IntentDockItems = [.. source.IntentDockItems],
        IntentDockStyle = source.IntentDockStyle,
    };

    private static UIPageSettings ClonePages(UIPageSettings source) => new()
    {
        Home = new UIHomePageSettings
        {
            HubHeroEnabled      = source.Home.HubHeroEnabled,
            HubHeroLayout       = source.Home.HubHeroLayout,
            ProgressCardsLayout = source.Home.ProgressCardsLayout,
            BentoColumns        = source.Home.BentoColumns,
            BentoTileStyle      = source.Home.BentoTileStyle,
            PendingFilesDisplay = source.Home.PendingFilesDisplay,
        },
        Preferences = new UIPreferencesPageSettings
        {
            PageEnabled      = source.Preferences.PageEnabled,
            TabBarLayout     = source.Preferences.TabBarLayout,
            GeneralTabLayout = source.Preferences.GeneralTabLayout,
            ColorSwatchCount = source.Preferences.ColorSwatchCount,
            PlaybackTabEnabled = source.Preferences.PlaybackTabEnabled,
        },
        ServerSettings = new UIServerSettingsPageSettings
        {
            PageEnabled      = source.ServerSettings.PageEnabled,
            TabBarLayout     = source.ServerSettings.TabBarLayout,
            TabContentLayout = source.ServerSettings.TabContentLayout,
        },
    };

    // ── Merge helpers (overlay non-default values) ─────────────────────────

    private static void MergeFeatures(UIFeatureFlags target, UIFeatureFlags source)
    {
        // Device features override global: we apply all values from the device
        // feature block since it represents the device's explicit defaults.
        target.CommandPalette    = source.CommandPalette;
        target.SearchButton      = source.SearchButton;
        target.ThemeToggle       = source.ThemeToggle;
        target.AvatarMenu        = source.AvatarMenu;
        target.ServerSettings    = source.ServerSettings;
        target.PendingFilesAlert = source.PendingFilesAlert;
        target.ViewToggle        = source.ViewToggle;
        target.ProfileSection    = source.ProfileSection;
        target.ColorPicker       = source.ColorPicker;
    }

    private static void MergeShell(UIShellSettings target, UIShellSettings source)
    {
        target.AppBarStyle     = source.AppBarStyle;
        target.LogoVariant     = source.LogoVariant;
        target.IntentDockItems = [.. source.IntentDockItems];
        target.IntentDockStyle = source.IntentDockStyle;
    }

    private static void MergePages(UIPageSettings target, UIPageSettings source)
    {
        // Home
        target.Home.HubHeroEnabled      = source.Home.HubHeroEnabled;
        target.Home.HubHeroLayout       = source.Home.HubHeroLayout;
        target.Home.ProgressCardsLayout = source.Home.ProgressCardsLayout;
        target.Home.BentoColumns        = source.Home.BentoColumns;
        target.Home.BentoTileStyle      = source.Home.BentoTileStyle;
        target.Home.PendingFilesDisplay = source.Home.PendingFilesDisplay;

        // Preferences
        target.Preferences.PageEnabled      = source.Preferences.PageEnabled;
        target.Preferences.TabBarLayout     = source.Preferences.TabBarLayout;
        target.Preferences.GeneralTabLayout = source.Preferences.GeneralTabLayout;
        target.Preferences.ColorSwatchCount = source.Preferences.ColorSwatchCount;
        target.Preferences.PlaybackTabEnabled = source.Preferences.PlaybackTabEnabled;

        // Server Settings
        target.ServerSettings.PageEnabled      = source.ServerSettings.PageEnabled;
        target.ServerSettings.TabBarLayout     = source.ServerSettings.TabBarLayout;
        target.ServerSettings.TabContentLayout = source.ServerSettings.TabContentLayout;
    }

    /// <summary>
    /// Sets a named feature flag by its snake_case JSON property name.
    /// Unknown names are silently ignored (forward-compatible).
    /// </summary>
    private static void SetFeatureFlag(UIFeatureFlags flags, string name, bool value)
    {
        switch (name.ToLowerInvariant())
        {
            case "command_palette":     flags.CommandPalette    = value; break;
            case "search_button":       flags.SearchButton      = value; break;
            case "theme_toggle":        flags.ThemeToggle       = value; break;
            case "avatar_menu":         flags.AvatarMenu        = value; break;
            case "server_settings":     flags.ServerSettings    = value; break;
            case "pending_files_alert": flags.PendingFilesAlert = value; break;
            case "view_toggle":         flags.ViewToggle        = value; break;
            case "profile_section":     flags.ProfileSection    = value; break;
            case "color_picker":        flags.ColorPicker       = value; break;
            // Unknown feature names silently ignored for forward compatibility
        }
    }
}
