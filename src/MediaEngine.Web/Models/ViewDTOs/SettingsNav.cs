using MudBlazor;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Settings page section identifiers — 5 groups, 16 screens.
/// Shared across Sidebar, MobileNavDrawer, and Settings page.
/// </summary>
public enum SettingsSection
{
    // ── Preferences (all users) ──
    Profile, Playback,

    // ── Providers (admin) ──
    ProviderConnections, ProviderPriority, WikidataConfig,

    // ── Intelligence (admin) ──
    AiModels, AiFeatures, VibeVocabulary, AiSchedule,

    // ── Library (admin) ──
    LibraryFolders,

    // ── Server (admin) ──
    StatusDashboard, Security, Users, Activity, Maintenance, Setup
}

/// <summary>A group of settings items (e.g. "Preferences", "Providers", "Server").</summary>
public sealed record SettingsGroupDef(
    string Key,
    string Label,
    string Icon,
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
/// referenced by multiple navigation components (Sidebar, MobileNavDrawer, Settings page).
/// </summary>
public static class SettingsNav
{
    /// <summary>All settings groups in display order.</summary>
    public static readonly SettingsGroupDef[] AllGroups =
    [
        new("Preferences", "Preferences",
            Icons.Material.Outlined.Person, false,
        [
            new(SettingsSection.Profile,  Icons.Material.Outlined.Person,   "Profile",  false, null),
            new(SettingsSection.Playback, Icons.Material.Outlined.PlayArrow, "Playback", false, null),
        ]),

        new("Providers", "Providers",
            Icons.Material.Outlined.Share, true,
        [
            new(SettingsSection.ProviderConnections, Icons.Material.Outlined.Hub,       "Connections", true, null),
            new(SettingsSection.ProviderPriority,    Icons.Material.Outlined.Sort,      "Priority",    true, null),
            new(SettingsSection.WikidataConfig,      Icons.Material.Outlined.Public,    "Wikidata",    true, null),
        ]),

        new("Intelligence", "Intelligence",
            Icons.Material.Outlined.Psychology, true,
        [
            new(SettingsSection.AiModels,       Icons.Material.Outlined.Memory,     "Models",     true, null),
            new(SettingsSection.AiFeatures,     Icons.Material.Outlined.ToggleOn,   "Features",   true, null),
            new(SettingsSection.VibeVocabulary,  Icons.Material.Outlined.Style,      "Vocabulary", true, null),
            new(SettingsSection.AiSchedule,     Icons.Material.Outlined.Schedule,   "Schedule",   true, null),
        ]),

        new("Library", "Library",
            Icons.Material.Outlined.Folder, true,
        [
            new(SettingsSection.LibraryFolders, Icons.Material.Outlined.FolderOpen, "Folders", true, null),
        ]),

        new("Server", "Server",
            Icons.Material.Outlined.Dns, true,
        [
            new(SettingsSection.StatusDashboard, Icons.Material.Outlined.Dashboard,  "Dashboard",   true, null),
            new(SettingsSection.Security,        Icons.Material.Outlined.VpnKey,     "Security",    true, null),
            new(SettingsSection.Users,           Icons.Material.Outlined.Group,      "Users",       true, null),
            new(SettingsSection.Activity,        Icons.Material.Outlined.Timeline,   "Activity",    true, null),
            new(SettingsSection.Maintenance,     Icons.Material.Outlined.Build,      "Maintenance", true, null),
            new(SettingsSection.Setup,           Icons.Material.Outlined.RocketLaunch, "Setup",     true, null),
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
    public static string RouteFor(SettingsSection section)
    {
        // Convert PascalCase to kebab-case for URLs.
        var name = section.ToString();
        var kebab = System.Text.RegularExpressions.Regex.Replace(name, "(?<!^)([A-Z])", "-$1").ToLowerInvariant();
        return $"/settings/{kebab}";
    }

    /// <summary>
    /// Parses a URL segment into a <see cref="SettingsSection"/>.
    /// Handles kebab-case (e.g. "provider-connections" → ProviderConnections) and legacy redirects.
    /// Returns null if the segment is not recognised.
    /// </summary>
    public static SettingsSection? ParseFromRoute(string? segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
            return null;

        var normalized = segment.Replace("-", "");

        if (Enum.TryParse<SettingsSection>(normalized, ignoreCase: true, out var parsed))
            return parsed;

        // Legacy route mappings — old routes redirect to new sections.
        return normalized.ToLowerInvariant() switch
        {
            "general"          => SettingsSection.Profile,
            "connectionvault"  => SettingsSection.ProviderConnections,
            "providers"        => SettingsSection.ProviderConnections,
            "propertymapper"   => SettingsSection.ProviderConnections,
            "universe"         => SettingsSection.WikidataConfig,
            "matchingpipeline" => SettingsSection.ProviderConnections,
            "apikeys"          => SettingsSection.Security,
            "library"          => SettingsSection.LibraryFolders,
            "servergeneral"    => SettingsSection.StatusDashboard,
            "connectivity"     => SettingsSection.StatusDashboard,
            "navigation"       => SettingsSection.Profile,
            "needsreview"      => SettingsSection.StatusDashboard,
            "conflicts"        => SettingsSection.StatusDashboard,
            _                  => null,
        };
    }

    private static bool IsAdminRole(string role) =>
        string.Equals(role, "Administrator", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "Curator", StringComparison.OrdinalIgnoreCase);
}
