using MudBlazor;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Canonical settings destinations rendered by the shared settings shell.
/// </summary>
public enum SettingsSection
{
    Overview,
    AdminOverview,
    Review,
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
    Encode,
    OfflineDownloads,
    Metadata,
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
    bool Deprecated = false,
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
        new(SettingsSection.AdminOverview, "admin", "admin", Icons.Material.Outlined.Dashboard, "Admin Overview", true, null, [], "json+sqlite"),
        new(SettingsSection.Review, "admin", "review", Icons.Material.Outlined.RateReview, "Review Queue", true, "review", []),
        new(SettingsSection.Playback, "user", "playback", Icons.Material.Outlined.PlayArrow, "Playback", false, null, [], "sqlite", Placeholder: true),
        new(SettingsSection.Folders, "admin", "folders", Icons.Material.Outlined.FolderOpen, "Libraries", true, null, []),
        new(SettingsSection.Metadata, "admin", "metadata", Icons.Material.Outlined.Schema, "Metadata", true, null, [], "json"),
        new(SettingsSection.Providers, "admin", "providers", Icons.Material.Outlined.Collections, "Providers", true, null, []),
        new(SettingsSection.Wikidata, "admin", "wikidata", Icons.Material.Outlined.Public, "Wikidata", true, null, []),
        new(SettingsSection.Models, "admin", "models", Icons.Material.Outlined.Memory, "AI Models", true, null, []),
        new(SettingsSection.Features, "admin", "features", Icons.Material.Outlined.ToggleOn, "AI Features", true, null, []),
        new(SettingsSection.Vocabulary, "admin", "vocabulary", Icons.Material.Outlined.Style, "AI Vocabulary", true, null, []),
        new(SettingsSection.Schedule, "admin", "schedule", Icons.Material.Outlined.Schedule, "AI Schedule", true, null, []),
        new(SettingsSection.System, "admin", "system", Icons.Material.Outlined.Dns, "Server", true, null, []),
        new(SettingsSection.Encode, "admin", "encode", Icons.Material.Outlined.VideoSettings, "Playback & Delivery", true, null, []),
        new(SettingsSection.OfflineDownloads, "admin", "offline-downloads", Icons.Material.Outlined.DownloadForOffline, "Offline Variants", true, null, []),
        new(SettingsSection.Security, "admin", "security", Icons.Material.Outlined.VpnKey, "Security", true, null, []),
        new(SettingsSection.Users, "admin", "users", Icons.Material.Outlined.Group, "Users", true, null, []),
        new(SettingsSection.Activity, "admin", "activity", Icons.Material.Outlined.Timeline, "Activity", true, null, []),
        new(SettingsSection.ProviderTester, "admin", "provider-tester", Icons.Material.Outlined.Biotech, "Provider Tester", true, null, [], "internal", Deprecated: true),
        new(SettingsSection.EnrichmentTester, "admin", "enrichment-tester", Icons.Material.Outlined.Science, "Enrichment Tester", true, null, ["tester"], "internal", Deprecated: true),
        new(SettingsSection.Maintenance, "admin", "maintenance", Icons.Material.Outlined.Build, "Maintenance", true, null, []),
        new(SettingsSection.Setup, "admin", "setup", Icons.Material.Outlined.RocketLaunch, "Setup", true, null, [], Placeholder: true),
    ];

    public static readonly SettingsTreeGroupDef[] TreeGroups =
    [
        new("user", "User Settings", Icons.Material.Outlined.Person, false, true, SettingsSection.Overview,
            [SettingsSection.Overview, SettingsSection.Playback]),
        new("admin", "Admin Settings", Icons.Material.Outlined.AdminPanelSettings, true, true, SettingsSection.AdminOverview,
            [
                SettingsSection.AdminOverview,
                SettingsSection.Review,
                SettingsSection.Folders,
                SettingsSection.Metadata,
                SettingsSection.Providers,
                SettingsSection.Wikidata,
                SettingsSection.Models,
                SettingsSection.Features,
                SettingsSection.Vocabulary,
                SettingsSection.Schedule,
                SettingsSection.System,
                SettingsSection.Encode,
                SettingsSection.OfflineDownloads,
                SettingsSection.Security,
                SettingsSection.Users,
                SettingsSection.Activity,
                SettingsSection.ProviderTester,
                SettingsSection.EnrichmentTester,
                SettingsSection.Maintenance,
                SettingsSection.Setup,
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
