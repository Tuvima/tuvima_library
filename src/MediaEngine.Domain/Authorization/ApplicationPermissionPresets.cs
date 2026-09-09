using System.Collections.Frozen;
using MediaEngine.Domain.Contracts;

namespace MediaEngine.Domain.Authorization;

public sealed record ApplicationPermissionPreset
{
    public ApplicationPermissionPreset(
        string id,
        string displayName,
        IEnumerable<ApplicationPermissionId> permissions,
        bool isAdministrator = false)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("A preset identifier is required.", nameof(id)) : id.Trim();
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? throw new ArgumentException("A display name is required.", nameof(displayName)) : displayName.Trim();
        Permissions = (permissions ?? throw new ArgumentNullException(nameof(permissions))).ToFrozenSet();
        IsAdministrator = isAdministrator;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public IReadOnlySet<ApplicationPermissionId> Permissions { get; }
    public bool IsAdministrator { get; }

    public IReadOnlySet<ApplicationPermissionId> ResolveAvailablePermissions(
        IPermissionRegistry registry,
        ApplicationType applicationType) =>
        (IsAdministrator
            ? registry.GetAvailableFor(applicationType).Select(definition => definition.Id)
            : Permissions.Where(id => registry.TryGet(id, out var definition) &&
                definition.IsAvailable && definition.ApplicationTypes.Contains(applicationType)))
        .ToFrozenSet();
}

public static class ApplicationPermissionPresets
{
    public static readonly ApplicationPermissionPreset MediaPlayer = new(
        "media-player", "Media Player", ApplicationPermissionIds.NativeClient.ToHashSet());

    public static readonly ApplicationPermissionPreset MonitoringAndAnalytics = new(
        "monitoring-analytics", "Monitoring & Analytics",
        Set(ApplicationPermissionIds.SystemStatusRead, ApplicationPermissionIds.SystemActivityRead,
            ApplicationPermissionIds.PlaybackSessionsRead, ApplicationPermissionIds.PlaybackHistoryRead,
            ApplicationPermissionIds.AnalyticsPlaybackRead, ApplicationPermissionIds.AnalyticsLibraryRead,
            ApplicationPermissionIds.AnalyticsUsersRead, ApplicationPermissionIds.AnalyticsDevicesRead));

    public static readonly ApplicationPermissionPreset HomeAutomation = new(
        "home-automation", "Home Automation",
        Set(ApplicationPermissionIds.SystemStatusRead, ApplicationPermissionIds.LibraryRead,
            ApplicationPermissionIds.PlaybackSessionsRead, ApplicationPermissionIds.PlaybackSessionsControl,
            ApplicationPermissionIds.EventsSubscribe));

    public static readonly ApplicationPermissionPreset MetadataIntegration = new(
        "metadata-integration", "Metadata Integration",
        Set(ApplicationPermissionIds.LibraryRead, ApplicationPermissionIds.MetadataRead,
            ApplicationPermissionIds.MetadataWrite, ApplicationPermissionIds.MetadataMatch,
            ApplicationPermissionIds.MetadataEnrichmentRead, ApplicationPermissionIds.MetadataEnrichmentRun));

    public static readonly ApplicationPermissionPreset ReadOnly = new(
        "read-only", "Read Only",
        Set(ApplicationPermissionIds.SystemStatusRead, ApplicationPermissionIds.LibraryRead,
            ApplicationPermissionIds.ArtworkRead, ApplicationPermissionIds.CollectionsRead));

    public static readonly ApplicationPermissionPreset Administrator = new(
        "administrator", "Administrator", [], isAdministrator: true);

    public static readonly ApplicationPermissionPreset Custom = new(
        "custom", "Custom", []);

    public static readonly IReadOnlyList<ApplicationPermissionPreset> All = Array.AsReadOnly<ApplicationPermissionPreset>(
        [MediaPlayer, MonitoringAndAnalytics, HomeAutomation, MetadataIntegration, ReadOnly, Administrator, Custom]);

    private static IReadOnlySet<ApplicationPermissionId> Set(params ApplicationPermissionId[] permissions) =>
        permissions.ToFrozenSet();
}
