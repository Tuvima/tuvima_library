using MudBlazor;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Canonical settings destinations rendered by the shared settings shell.
/// </summary>
public enum SettingsSection
{
    Overview,
    Playback,
    Privacy,

    AdminOverview,
    Libraries,
    Ingestion,
    Metadata,
    Providers,
    LocalAi,
    Plugins,
    Delivery,
    Access,

    Review,
    Setup,
    ProviderTester,
    EnrichmentTester,
}

/// <summary>A primary settings destination group.</summary>
public sealed record SettingsGroupDef(
    string Key,
    string Label,
    string Icon,
    bool AdminOnly,
    SettingsSection DefaultSection);

/// <summary>A single settings route shown in the settings sidebar tree.</summary>
public sealed record SettingsItemDef(
    SettingsSection Value,
    string GroupKey,
    string? Slug,
    string Icon,
    string Label,
    bool AdminOnly,
    string? BadgeKey,
    IReadOnlyList<string> Aliases,
    string Source = "mixed",
    bool Placeholder = false);

/// <summary>A grouped sidebar node with expandable child settings routes.</summary>
public sealed record SettingsTreeGroupDef(
    string Key,
    string Label,
    string Icon,
    bool AdminOnly,
    bool Expandable,
    SettingsSection DefaultSection,
    IReadOnlyList<SettingsSection> Sections);

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
/// Explicit route map for the Settings shell.
/// Keeps canonical slugs, aliases, grouping, and role visibility in one place.
/// </summary>
public static class SettingsNav
{
    public static readonly SettingsGroupDef[] AllGroups =
    [
        new("user", "User Settings", Icons.Material.Outlined.Person, false, SettingsSection.Overview),
        new("admin", "Admin Settings", Icons.Material.Outlined.AdminPanelSettings, true, SettingsSection.AdminOverview),
    ];

    public static readonly SettingsItemDef[] AllItems =
    [
        new(SettingsSection.Overview, "user", null, Icons.Material.Outlined.Person, "User Overview", false, null, [], "sqlite"),
        new(SettingsSection.Playback, "user", "playback", Icons.Material.Outlined.MenuBook, "Playback & Reading", false, null, [], "sqlite"),
        new(SettingsSection.Privacy, "user", "privacy", Icons.Material.Outlined.PrivacyTip, "Privacy & History", false, null, [], "sqlite"),

        new(SettingsSection.AdminOverview, "admin", "admin", Icons.Material.Outlined.Dashboard, "Admin Overview", true, null, [], "json+sqlite"),
        new(SettingsSection.Libraries, "admin", "libraries", Icons.Material.Outlined.FolderOpen, "Libraries", true, null, ["folders"]),
        new(SettingsSection.Ingestion, "admin", "ingestion", Icons.Material.Outlined.PendingActions, "Ingestion & Tasks", true, null, ["activity", "registry", "tasks", "maintenance"]),
        new(SettingsSection.Metadata, "admin", "metadata", Icons.Material.Outlined.Schema, "Metadata & Matching", true, null, ["wikidata"], "json"),
        new(SettingsSection.Providers, "admin", "providers", Icons.Material.Outlined.Collections, "Providers", true, null, []),
        new(SettingsSection.LocalAi, "admin", "ai", Icons.Material.Outlined.Memory, "Local AI", true, null, ["models", "features", "vocabulary", "schedule"]),
        new(SettingsSection.Plugins, "admin", "plugins", Icons.Material.Outlined.Extension, "Plugins", true, null, ["extensions", "commercial-skip", "intro-skip", "credits"], "sqlite"),
        new(SettingsSection.Delivery, "admin", "delivery", Icons.Material.Outlined.VideoSettings, "Playback & Delivery", true, null, ["encode", "offline-downloads"]),
        new(SettingsSection.Access, "admin", "access", Icons.Material.Outlined.AdminPanelSettings, "Users & Access", true, null, ["users", "security", "apikeys", "api-keys"]),

        new(SettingsSection.Review, "admin", "review", Icons.Material.Outlined.RateReview, "Review Queue", true, "review", ["needsreview", "needs-review"], "mixed"),
        new(SettingsSection.Setup, "admin", "setup", Icons.Material.Outlined.RocketLaunch, "Setup", true, null, [], Placeholder: true),
        new(SettingsSection.ProviderTester, "admin", "provider-tester", Icons.Material.Outlined.Biotech, "Provider Tester", true, null, [], "internal"),
        new(SettingsSection.EnrichmentTester, "admin", "enrichment-tester", Icons.Material.Outlined.Science, "Enrichment Tester", true, null, ["tester"], "internal"),
    ];

    public static readonly SettingsTreeGroupDef[] TreeGroups =
    [
        new("user", "User Settings", Icons.Material.Outlined.Person, false, true, SettingsSection.Overview,
            [SettingsSection.Overview, SettingsSection.Playback, SettingsSection.Privacy]),
        new("admin", "Admin Settings", Icons.Material.Outlined.AdminPanelSettings, true, true, SettingsSection.AdminOverview,
            [
                SettingsSection.AdminOverview,
                SettingsSection.Libraries,
                SettingsSection.Ingestion,
                SettingsSection.Metadata,
                SettingsSection.Providers,
                SettingsSection.LocalAi,
                SettingsSection.Plugins,
                SettingsSection.Delivery,
                SettingsSection.Access,
            ]),
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
            .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, SettingsGroupDef> _groupsByKey =
        AllGroups.ToDictionary(group => group.Key, StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<SettingsGroupDef> FilteredGroups(string role)
    {
        var hasAdmin = IsAdminRole(role);
        return AllGroups.Where(group => !group.AdminOnly || hasAdmin);
    }

    public static IEnumerable<SettingsTreeGroupDef> FilteredTreeGroups(string role)
    {
        var hasAdmin = IsAdminRole(role);
        return TreeGroups
            .Where(group => !group.AdminOnly || hasAdmin)
            .Where(group => group.Sections.Any(section => IsVisible(section, role)));
    }

    public static IReadOnlyList<SettingsItemDef> FilteredTreeItems(SettingsTreeGroupDef group, string role) =>
        group.Sections
            .Select(GetItem)
            .Where(item => IsVisible(item.Value, role))
            .ToList();

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
        IsAdminRole(role)
            ? SettingsSection.Overview
            : AllItems.First(item => IsVisible(item.Value, role)).Value;

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
