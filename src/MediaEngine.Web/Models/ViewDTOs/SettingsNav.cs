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
    DevHarness,
    Providers,
    LocalAi,
    Plugins,
    Delivery,
    Access,
    Server,

    ActivityLogs,
    Review,
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
    bool Placeholder = false,
    SettingsStatusKind Status = SettingsStatusKind.Live);

/// <summary>A URL-addressable destination nested beneath a settings section.</summary>
public sealed record SettingsSubsectionDef(
    string Slug,
    string Label,
    string Icon);

/// <summary>A grouped sidebar node with expandable child settings routes.</summary>
public sealed record SettingsTreeGroupDef(
    string Key,
    string Label,
    string Icon,
    bool AdminOnly,
    bool Expandable,
    SettingsSection DefaultSection,
    IReadOnlyList<SettingsSection> Sections,
    string? ParentKey = null);

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
        new("personal", "Personal", Icons.Material.Outlined.Person, false, SettingsSection.Overview),
        new("administration", "Administration", Icons.Material.Outlined.AdminPanelSettings, true, SettingsSection.AdminOverview),
        new("advanced", "Advanced", Icons.Material.Outlined.Tune, true, SettingsSection.LocalAi),
    ];

    public static readonly SettingsItemDef[] AllItems =
    [
        new(SettingsSection.Overview, "personal", "profile", Icons.Material.Outlined.Person, "Profile", false, null, [], "sqlite"),
        new(SettingsSection.Playback, "personal", "playback", Icons.Material.Outlined.PlayCircleOutline, "Playback & Reading", false, null, [], "sqlite"),
        new(SettingsSection.Privacy, "personal", "privacy", Icons.Material.Outlined.Lock, "Privacy & Data", false, null, [], "unavailable", Placeholder: true),

        new(SettingsSection.AdminOverview, "administration", "system", Icons.Material.Outlined.Dashboard, "System Overview", true, null, [], "json+sqlite", Status: SettingsStatusKind.Live),
        new(SettingsSection.Libraries, "administration", "media-management", Icons.Material.Outlined.PermMedia, "Media Management", true, null, [], Status: SettingsStatusKind.Live),
        new(SettingsSection.Providers, "administration", "providers", Icons.Material.Outlined.Hub, "Metadata", true, null, [], Status: SettingsStatusKind.Live),
        new(SettingsSection.Review, "administration", "review", Icons.Material.Outlined.RateReview, "Needs Review", true, "review", [], "mixed"),
        new(SettingsSection.ActivityLogs, "administration", "activity", Icons.Material.Outlined.Timeline, "Activity & Audit", true, null, [], "sqlite"),
        new(SettingsSection.Delivery, "administration", "delivery", Icons.Material.Outlined.VideoSettings, "Playback & Delivery", true, null, [], Status: SettingsStatusKind.Partial),
        new(SettingsSection.Access, "administration", "access", Icons.Material.Outlined.Group, "Users & Access", true, null, [], Status: SettingsStatusKind.Partial),
        new(SettingsSection.Server, "administration", "backup-recovery", Icons.Material.Outlined.Backup, "Backup & Recovery", true, null, [], Status: SettingsStatusKind.Live),

        new(SettingsSection.LocalAi, "advanced", "ai", Icons.Material.Outlined.Memory, "Local AI", true, null, [], Status: SettingsStatusKind.Live),
        new(SettingsSection.Plugins, "advanced", "plugins", Icons.Material.Outlined.Extension, "Plugins", true, null, [], "sqlite", Status: SettingsStatusKind.Partial),
        new(SettingsSection.DevHarness, "advanced", "developer", Icons.Material.Outlined.Construction, "Developer Tools", true, null, ["dev-harness", "harness", "ingestion-harness", "test-harness"], "internal", Status: SettingsStatusKind.Partial),
        new(SettingsSection.ProviderTester, "advanced", "provider-tester", Icons.Material.Outlined.Biotech, "Provider Tester", true, null, [], "internal"),
        new(SettingsSection.EnrichmentTester, "advanced", "enrichment-tester", Icons.Material.Outlined.Science, "Enrichment Tester", true, null, ["tester"], "internal"),
    ];

    public static readonly SettingsTreeGroupDef[] TreeGroups =
    [
        new("personal", "Personal", Icons.Material.Outlined.Person, false, false, SettingsSection.Overview,
            [SettingsSection.Overview, SettingsSection.Playback, SettingsSection.Privacy]),
        new("administration", "Administration", Icons.Material.Outlined.AdminPanelSettings, true, false, SettingsSection.AdminOverview,
            [
                SettingsSection.AdminOverview,
                SettingsSection.Libraries,
                SettingsSection.Providers,
                SettingsSection.Review,
                SettingsSection.ActivityLogs,
                SettingsSection.Delivery,
                SettingsSection.Access,
                SettingsSection.Server,
            ]),
        new("advanced", "Advanced", Icons.Material.Outlined.Tune, true, false, SettingsSection.LocalAi,
            [SettingsSection.LocalAi, SettingsSection.Plugins, SettingsSection.DevHarness]),
    ];

    public static readonly IReadOnlyDictionary<SettingsSection, IReadOnlyList<SettingsSubsectionDef>> Subsections =
        new Dictionary<SettingsSection, IReadOnlyList<SettingsSubsectionDef>>
        {
            [SettingsSection.Overview] = [],
            [SettingsSection.Playback] = [],
            [SettingsSection.Privacy] =
            [
                new("history", "Personal History", Icons.Material.Outlined.History),
                new("tracking", "Tracking", Icons.Material.Outlined.Timeline),
                new("personalization", "Personalization", Icons.Material.Outlined.AutoAwesome),
                new("export-reset", "Export & Reset", Icons.Material.Outlined.SettingsBackupRestore),
            ],
            [SettingsSection.AdminOverview] = [],
            [SettingsSection.Libraries] =
            [
                new("incoming", "Incoming", Icons.Material.Outlined.MoveToInbox),
                new("libraries", "Libraries", Icons.Material.Outlined.FolderOpen),
                new("activity", "Activity", Icons.Material.Outlined.Timeline),
            ],
            [SettingsSection.DevHarness] =
            [
                new("options", "Run Options", Icons.Material.Outlined.Tune),
                new("harnesses", "Harnesses", Icons.Material.Outlined.Construction),
                new("result", "Last Result", Icons.Material.Outlined.FactCheck),
            ],
            [SettingsSection.Providers] =
            [
                new("enrichment", "Enrichment", Icons.Material.Outlined.AutoAwesome),
                new("priority", "Source Priority", Icons.Material.Outlined.SwapVert),
                new("health", "Health", Icons.Material.Outlined.MonitorHeart),
            ],
            [SettingsSection.ActivityLogs] =
            [
                new("batches", "Batches", Icons.Material.Outlined.FolderCopy),
                new("people", "People", Icons.Material.Outlined.People),
                new("maintenance", "Maintenance & Retention", Icons.Material.Outlined.DeleteSweep),
            ],
            [SettingsSection.LocalAi] =
            [
                new("models", "Models & Runtime", Icons.Material.Outlined.Storage),
                new("vocabulary", "Vocabulary", Icons.Material.Outlined.Spellcheck),
                new("automation", "Automation", Icons.Material.Outlined.Schedule),
            ],
            [SettingsSection.Plugins] =
            [
                new("jobs-health", "Health & Jobs", Icons.Material.Outlined.HealthAndSafety),
                new("catalog", "Approved Catalog", Icons.Material.Outlined.Verified),
                new("capabilities", "Capabilities", Icons.Material.Outlined.CheckCircleOutline),
                new("danger", "Danger Zone", Icons.Material.Outlined.Delete),
            ],
            [SettingsSection.Delivery] =
            [
                new("scheduling", "Scheduling", Icons.Material.Outlined.Schedule),
                new("storage", "Variant Storage", Icons.Material.Outlined.Storage),
                new("active-jobs", "Active Jobs", Icons.Material.Outlined.PendingActions),
                new("diagnostics", "Diagnostics", Icons.Material.Outlined.MonitorHeart),
            ],
            [SettingsSection.Access] =
            [
                new("authentication", "Authentication", Icons.Material.Outlined.AdminPanelSettings),
                new("api-keys", "Guest API Keys", Icons.Material.Outlined.Key),
                new("session-policy", "Session Policy", Icons.Material.Outlined.Policy),
            ],
            [SettingsSection.Server] = [],
            [SettingsSection.Review] = [],
            [SettingsSection.ProviderTester] = [new("overview", "Overview", Icons.Material.Outlined.Biotech)],
            [SettingsSection.EnrichmentTester] = [new("overview", "Overview", Icons.Material.Outlined.Science)],
        };

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

    private static readonly HashSet<SettingsSection> _landingSections =
    [
        SettingsSection.Overview,
        SettingsSection.Playback,
        SettingsSection.AdminOverview,
        SettingsSection.Libraries,
        SettingsSection.Providers,
        SettingsSection.Review,
        SettingsSection.ActivityLogs,
        SettingsSection.Delivery,
        SettingsSection.Access,
        SettingsSection.Server,
        SettingsSection.LocalAi,
        SettingsSection.Plugins,
        SettingsSection.ProviderTester,
        SettingsSection.EnrichmentTester,
    ];

    public static IEnumerable<SettingsGroupDef> FilteredGroups(string role)
    {
        var hasAdmin = IsAdminRole(role);
        return AllGroups.Where(group => !group.AdminOnly || hasAdmin);
    }

    public static IEnumerable<SettingsTreeGroupDef> FilteredTreeGroups(string role)
    {
        var hasAdmin = IsAdminRole(role);
        return TreeGroups
            .Where(group => string.IsNullOrWhiteSpace(group.ParentKey))
            .Where(group => !group.AdminOnly || hasAdmin)
            .Where(group => group.Sections.Any(section => IsVisible(section, role))
                            || FilteredChildTreeGroups(group, role).Any());
    }

    public static IEnumerable<SettingsTreeGroupDef> FilteredChildTreeGroups(SettingsTreeGroupDef parent, string role)
    {
        var hasAdmin = IsAdminRole(role);
        return TreeGroups
            .Where(group => string.Equals(group.ParentKey, parent.Key, StringComparison.OrdinalIgnoreCase))
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

    public static SettingsStatusKind GetStatus(SettingsSection section) => GetItem(section).Status;

    public static SettingsGroupDef GetGroup(SettingsSection section) => _groupsByKey[GetItem(section).GroupKey];

    public static SettingsSection GetDefaultSection(string groupKey) => _groupsByKey[groupKey].DefaultSection;

    public static bool IsVisible(SettingsSection section, string role)
    {
        if (section == SettingsSection.Privacy)
        {
            return false;
        }

        if (IsAdministratorRole(role))
        {
            return true;
        }

        if (IsCuratorRole(role))
        {
            return section is SettingsSection.Overview
                or SettingsSection.Playback
                or SettingsSection.Review
                or SettingsSection.ActivityLogs;
        }

        return section is SettingsSection.Overview or SettingsSection.Playback;
    }

    public static SettingsSection FirstVisibleSection(string role) =>
        AllItems.First(item => IsVisible(item.Value, role)).Value;

    public static string RouteFor(SettingsSection section)
    {
        var sectionRoute = SectionRouteFor(section);
        if (_landingSections.Contains(section))
        {
            return sectionRoute;
        }

        var defaultSubsection = GetSubsections(section).FirstOrDefault();
        return defaultSubsection is null
            ? sectionRoute
            : $"{sectionRoute}/{defaultSubsection.Slug}";
    }

    public static IReadOnlyList<SettingsSubsectionDef> GetSubsections(SettingsSection section) =>
        Subsections.TryGetValue(section, out var subsections) ? subsections : [];

    public static SettingsSubsectionDef GetDefaultSubsection(SettingsSection section) =>
        GetSubsections(section).First();

    public static SettingsSubsectionDef? ResolveSubsection(SettingsSection section, string? slug)
    {
        var subsections = GetSubsections(section);
        if (string.IsNullOrWhiteSpace(slug))
        {
            return subsections.FirstOrDefault();
        }

        var normalized = NormalizeKey(slug);
        normalized = (section, normalized) switch
        {
            (SettingsSection.LocalAi, "runtime") => "models",
            (SettingsSection.LocalAi, "schedule") => "automation",
            _ => normalized,
        };
        return subsections.FirstOrDefault(subsection =>
            string.Equals(NormalizeKey(subsection.Slug), normalized, StringComparison.OrdinalIgnoreCase));
    }

    public static string RouteFor(SettingsSection section, string subsectionSlug)
    {
        var subsection = ResolveSubsection(section, subsectionSlug)
            ?? throw new ArgumentOutOfRangeException(nameof(subsectionSlug), subsectionSlug, "Unknown settings subsection.");

        if (GetSubsections(section).Count == 0)
        {
            return SectionRouteFor(section);
        }

        return $"{SectionRouteFor(section)}/{subsection.Slug}";
    }

    public static SettingsRouteResolution ResolveRoute(string? segment, string role)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return new SettingsRouteResolution(
                SettingsSection.Overview,
                RouteFor(SettingsSection.Overview),
                IsCanonicalRoute: false,
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

        return new SettingsRouteResolution(
            FirstVisibleSection(role),
            "/not-found",
            IsCanonicalRoute: false,
            IsKnownRoute: false,
            RequestedSectionAllowed: false);
    }

    public static SettingsSection? ParseFromRoute(string? segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return SettingsSection.Overview;
        }

        var resolution = ResolveRoute(segment, "Administrator");
        return resolution.IsKnownRoute ? resolution.Section : null;
    }

    private static string NormalizeKey(string value)
    {
        var chars = value.Where(char.IsLetterOrDigit).ToArray();
        return new string(chars).ToLowerInvariant();
    }

    private static string SectionRouteFor(SettingsSection section)
    {
        var item = GetItem(section);
        return $"/settings/{item.Slug}";
    }

    private static bool IsAdminRole(string role) =>
        IsAdministratorRole(role) || IsCuratorRole(role);

    private static bool IsAdministratorRole(string role) =>
        string.Equals(role, "Administrator", StringComparison.OrdinalIgnoreCase);

    private static bool IsCuratorRole(string role) =>
        string.Equals(role, "Curator", StringComparison.OrdinalIgnoreCase);
}
