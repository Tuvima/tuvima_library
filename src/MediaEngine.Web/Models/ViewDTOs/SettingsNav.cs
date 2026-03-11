using MudBlazor;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Settings page section identifiers.
/// Moved from SettingsSidebar to be shared across LeftDock, MobileNavDrawer, and Settings page.
/// </summary>
public enum SettingsSection
{
    // Preferences
    General, Playback, Navigation,
    // Metadata
    ConnectionVault, PropertyMapper, Universe, MatchingPipeline, NeedsReview, Activity,
    // Server
    ServerGeneral, Library, Connectivity, ApiKeys, Conflicts, Users, Maintenance,
    // Legacy alias — kept so existing deep links to /settings/providers redirect correctly.
    Providers
}

/// <summary>A group of settings items (e.g. "Preferences", "Metadata", "Server").</summary>
public sealed record SettingsGroupDef(
    string Key,
    string Label,
    bool AdminOnly,
    SettingsItemDef[] Items);

/// <summary>A single settings navigation item.</summary>
public sealed record SettingsItemDef(
    SettingsSection Value,
    string Icon,
    string Label,
    bool AdminOnly,
    string? BadgeKey);

/// <summary>
/// Static registry of all settings navigation groups and items.
/// Follows the <see cref="ContentLanes"/> pattern: a static data source
/// referenced by multiple navigation components (LeftDock, MobileNavDrawer, Settings page).
/// </summary>
public static class SettingsNav
{
    /// <summary>All settings groups in display order.</summary>
    public static readonly SettingsGroupDef[] AllGroups =
    [
        new("Preferences", "Preferences", false,
        [
            new(SettingsSection.General,    Icons.Material.Outlined.Tune,      "General",    false, null),
            new(SettingsSection.Playback,   Icons.Material.Outlined.PlayArrow, "Playback",   false, null),
        ]),

        new("Metadata", "Metadata", true,
        [
            new(SettingsSection.ConnectionVault, Icons.Material.Outlined.Hub,        "Metadata",      true, null),
            new(SettingsSection.NeedsReview,     Icons.Material.Outlined.RateReview, "Needs Review",  true, "review"),
        ]),

        new("Server", "Server", true,
        [
            new(SettingsSection.ServerGeneral, Icons.Material.Outlined.Dns,        "General",      true, null),
            new(SettingsSection.Library,       Icons.Material.Outlined.FolderOpen, "Library",      true, null),
            new(SettingsSection.Activity,      Icons.Material.Outlined.Timeline,   "Activity",     true, null),
            new(SettingsSection.Connectivity,  Icons.Material.Outlined.Wifi,       "Connectivity", true, null),
            new(SettingsSection.ApiKeys,       Icons.Material.Outlined.VpnKey,     "API Keys",     true, null),
            new(SettingsSection.Users,         Icons.Material.Outlined.Group,      "Users",        true, null),
            new(SettingsSection.Maintenance,   Icons.Material.Outlined.Build,      "Maintenance",  true, null),
        ]),
    ];

    /// <summary>Returns groups visible to the given role.</summary>
    public static IEnumerable<SettingsGroupDef> FilteredGroups(string role)
    {
        var hasAdmin = IsAdminRole(role);
        return AllGroups.Where(g => !g.AdminOnly || hasAdmin);
    }

    /// <summary>Returns items within a group visible to the given role.</summary>
    public static List<SettingsItemDef> FilteredItems(SettingsGroupDef group, string role)
    {
        var hasAdmin = IsAdminRole(role);
        return group.Items.Where(i => !i.AdminOnly || hasAdmin).ToList();
    }

    /// <summary>Returns the URL route for a settings section.</summary>
    public static string RouteFor(SettingsSection section) =>
        $"/settings/{section.ToString().ToLowerInvariant()}";

    /// <summary>
    /// Parses a URL segment into a <see cref="SettingsSection"/>.
    /// Handles kebab-case (e.g. "apikeys" → ApiKeys) and legacy redirects.
    /// Returns null if the segment is not recognised.
    /// </summary>
    public static SettingsSection? ParseFromRoute(string? segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
            return null;

        var normalized = segment.Replace("-", "");

        if (!Enum.TryParse<SettingsSection>(normalized, ignoreCase: true, out var parsed))
            return null;

        // Redirect legacy routes.
        if (parsed == SettingsSection.Providers)
            parsed = SettingsSection.ConnectionVault;

        return parsed;
    }

    private static bool IsAdminRole(string role) =>
        string.Equals(role, "Administrator", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "Curator", StringComparison.OrdinalIgnoreCase);
}
