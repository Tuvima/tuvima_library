using MudBlazor;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Canonical settings destinations rendered by the shared settings shell.
/// </summary>
public enum SettingsSection
{
    Overview,
    Review,
    Profile,
    Playback,
    Folders,
    Providers,
    Wikidata,
    Models,
    Features,
    Vocabulary,
    Schedule,
    System,
    Security,
    Users,
    Activity,
    Maintenance,
    Setup,
}

/// <summary>A primary settings destination shown in the top tab row.</summary>
public sealed record SettingsGroupDef(
    string Key,
    string Label,
    string Icon,
    bool AdminOnly,
    SettingsSection DefaultSection);

/// <summary>A single settings route shown in the secondary tab row.</summary>
public sealed record SettingsItemDef(
    SettingsSection Value,
    string GroupKey,
    string? Slug,
    string Icon,
    string Label,
    bool AdminOnly,
    string? BadgeKey,
    IReadOnlyList<string> Aliases);

/// <summary>
/// Result of resolving a route segment into a settings destination.
/// </summary>
public sealed record SettingsRouteResolution(
    SettingsSection Section,
    string CanonicalRoute,
    bool IsCanonicalRoute,
    bool IsKnownRoute,
    bool RequestedSectionAllowed)
{
    public bool ShouldRedirect => !IsCanonicalRoute || !RequestedSectionAllowed;
}

/// <summary>
/// Explicit route registry for the Settings shell.
/// Keeps canonical slugs, aliases, grouping, and role visibility in one place.
/// </summary>
public static class SettingsNav
{
    public static readonly SettingsGroupDef[] AllGroups =
    [
        new("overview",  "Overview",  Icons.Material.Outlined.Dashboard, false, SettingsSection.Overview),
        new("review",    "Review",    Icons.Material.Outlined.RateReview, false, SettingsSection.Review),
        new("personal",  "Personal",  Icons.Material.Outlined.Person, false, SettingsSection.Profile),
        new("library",   "Library",   Icons.Material.Outlined.FolderOpen, true, SettingsSection.Folders),
        new("providers", "Providers", Icons.Material.Outlined.Collections, true, SettingsSection.Providers),
        new("ai",        "AI",        Icons.Material.Outlined.Memory, true, SettingsSection.Models),
        new("server",    "Server",    Icons.Material.Outlined.Dns, true, SettingsSection.System),
    ];

    public static readonly SettingsItemDef[] AllItems =
    [
        new(SettingsSection.Overview,   "overview",  null,           Icons.Material.Outlined.Dashboard,   "Overview",    false, null, []),
        new(SettingsSection.Review,     "review",    "review",       Icons.Material.Outlined.RateReview,  "Review",      false, "review", ["needsreview", "conflicts"]),
        new(SettingsSection.Profile,    "personal",  "profile",      Icons.Material.Outlined.Person,      "Profile",     false, null, ["general", "navigation"]),
        new(SettingsSection.Playback,   "personal",  "playback",     Icons.Material.Outlined.PlayArrow,   "Playback",    false, null, []),
        new(SettingsSection.Folders,    "library",   "folders",      Icons.Material.Outlined.FolderOpen,  "Folders",     true,  null, ["library", "libraries"]),
        new(SettingsSection.Providers,  "providers", "providers",    Icons.Material.Outlined.Collections, "Providers",   true,  null, ["providerpriority", "providerconnections", "providerprioritytab", "connections", "connectionvault", "matchingpipeline", "propertymapper"]),
        new(SettingsSection.Wikidata,   "providers", "wikidata",     Icons.Material.Outlined.Public,      "Wikidata",    true,  null, ["wikidataconfig", "universe", "universesettings", "universeconfig"]),
        new(SettingsSection.Models,     "ai",        "models",       Icons.Material.Outlined.Memory,      "Models",      true,  null, ["ai", "aimodels"]),
        new(SettingsSection.Features,   "ai",        "features",     Icons.Material.Outlined.ToggleOn,    "Features",    true,  null, ["aifeatures"]),
        new(SettingsSection.Vocabulary, "ai",        "vocabulary",   Icons.Material.Outlined.Style,       "Vocabulary",  true,  null, ["vibevocabulary", "vocab"]),
        new(SettingsSection.Schedule,   "ai",        "schedule",     Icons.Material.Outlined.Schedule,    "Schedule",    true,  null, ["aischedule"]),
        new(SettingsSection.System,     "server",    "system",       Icons.Material.Outlined.Dns,         "System",      true,  null, ["server", "statusdashboard", "dashboard", "servergeneral", "connectivity"]),
        new(SettingsSection.Security,   "server",    "security",     Icons.Material.Outlined.VpnKey,      "Security",    true,  null, ["apikeys"]),
        new(SettingsSection.Users,      "server",    "users",        Icons.Material.Outlined.Group,       "Users",       true,  null, []),
        new(SettingsSection.Activity,   "server",    "activity",     Icons.Material.Outlined.Timeline,    "Activity",    true,  null, []),
        new(SettingsSection.Maintenance,"server",    "maintenance",  Icons.Material.Outlined.Build,       "Maintenance", true,  null, []),
        new(SettingsSection.Setup,      "server",    "setup",        Icons.Material.Outlined.RocketLaunch,"Setup",       true,  null, []),
    ];

    private static readonly Dictionary<SettingsSection, SettingsItemDef> _itemsBySection =
        AllItems.ToDictionary(item => item.Value);

    private static readonly Dictionary<string, SettingsItemDef> _itemsBySlug =
        AllItems
            .Where(item => !string.IsNullOrWhiteSpace(item.Slug))
            .ToDictionary(item => NormalizeKey(item.Slug!), item => item, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, SettingsItemDef> _itemsByAlias =
        AllItems
            .SelectMany(item => item.Aliases.Select(alias => new KeyValuePair<string, SettingsItemDef>(NormalizeKey(alias), item)))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, SettingsGroupDef> _groupsByKey =
        AllGroups.ToDictionary(group => group.Key, StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<SettingsGroupDef> FilteredGroups(string role)
    {
        var hasAdmin = IsAdminRole(role);
        return AllGroups.Where(group => !group.AdminOnly || hasAdmin);
    }

    public static IReadOnlyList<SettingsItemDef> FilteredItems(SettingsGroupDef group, string role)
    {
        var hasAdmin = IsAdminRole(role);
        return AllItems
            .Where(item => string.Equals(item.GroupKey, group.Key, StringComparison.OrdinalIgnoreCase))
            .Where(item => !item.AdminOnly || hasAdmin)
            .ToList();
    }

    public static SettingsItemDef GetItem(SettingsSection section) => _itemsBySection[section];

    public static SettingsGroupDef GetGroup(SettingsSection section) => _groupsByKey[GetItem(section).GroupKey];

    public static SettingsSection GetDefaultSection(string groupKey) => _groupsByKey[groupKey].DefaultSection;

    public static bool IsVisible(SettingsSection section, string role)
    {
        var item = GetItem(section);
        return !item.AdminOnly || IsAdminRole(role);
    }

    public static SettingsSection FirstVisibleSection(string role) =>
        AllItems.First(item => IsVisible(item.Value, role)).Value;

    public static string RouteFor(SettingsSection section)
    {
        var item = GetItem(section);
        return string.IsNullOrWhiteSpace(item.Slug)
            ? "/settings"
            : $"/settings/{item.Slug}";
    }

    public static SettingsRouteResolution ResolveRoute(string? segment, string role)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return new SettingsRouteResolution(
                SettingsSection.Overview,
                RouteFor(SettingsSection.Overview),
                IsCanonicalRoute: true,
                IsKnownRoute: true,
                RequestedSectionAllowed: true);
        }

        var normalized = NormalizeKey(segment);

        if (_itemsBySlug.TryGetValue(normalized, out var canonicalItem))
        {
            if (IsVisible(canonicalItem.Value, role))
            {
                return new SettingsRouteResolution(
                    canonicalItem.Value,
                    RouteFor(canonicalItem.Value),
                    IsCanonicalRoute: true,
                    IsKnownRoute: true,
                    RequestedSectionAllowed: true);
            }

            var fallback = FirstVisibleSection(role);
            return new SettingsRouteResolution(
                fallback,
                RouteFor(fallback),
                IsCanonicalRoute: false,
                IsKnownRoute: true,
                RequestedSectionAllowed: false);
        }

        if (_itemsByAlias.TryGetValue(normalized, out var aliasedItem))
        {
            if (IsVisible(aliasedItem.Value, role))
            {
                return new SettingsRouteResolution(
                    aliasedItem.Value,
                    RouteFor(aliasedItem.Value),
                    IsCanonicalRoute: false,
                    IsKnownRoute: true,
                    RequestedSectionAllowed: true);
            }

            var fallback = FirstVisibleSection(role);
            return new SettingsRouteResolution(
                fallback,
                RouteFor(fallback),
                IsCanonicalRoute: false,
                IsKnownRoute: true,
                RequestedSectionAllowed: false);
        }

        var unknownFallback = FirstVisibleSection(role);
        return new SettingsRouteResolution(
            unknownFallback,
            RouteFor(unknownFallback),
            IsCanonicalRoute: false,
            IsKnownRoute: false,
            RequestedSectionAllowed: false);
    }

    public static SettingsSection? ParseFromRoute(string? segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
            return SettingsSection.Overview;

        var resolution = ResolveRoute(segment, "Administrator");
        return resolution.IsKnownRoute ? resolution.Section : null;
    }

    private static string NormalizeKey(string value)
    {
        var chars = value.Where(char.IsLetterOrDigit).ToArray();
        return new string(chars).ToLowerInvariant();
    }

    private static bool IsAdminRole(string role) =>
        string.Equals(role, "Administrator", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "Curator", StringComparison.OrdinalIgnoreCase);
}
