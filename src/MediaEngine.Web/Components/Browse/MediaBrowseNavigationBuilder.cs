using MediaEngine.Web.Models.ViewDTOs;

namespace MediaEngine.Web.Components.Browse;

/// <summary>
/// Creates persistent lane navigation from the same browse-mode configuration used by the toolbar.
/// Keeping both surfaces configuration-driven prevents sidebar shortcuts from drifting from available modes.
/// </summary>
public static class MediaBrowseNavigationBuilder
{
    public static MediaSectionNavigationGroup BuildBrowseGroup(LibraryBrowsePreset preset)
        => BuildBrowseGroup(preset, "Browse as", null);

    public static MediaSectionNavigationGroup BuildBrowseGroup(
        LibraryBrowsePreset preset,
        string label,
        IReadOnlySet<string>? tabIds)
    {
        var items = preset.Tabs
            .Where(tab => tabIds is null || tabIds.Contains(tab.Id))
            .SelectMany(tab => tab.GroupingOptions.Select(option => new { Tab = tab, Option = option }))
            .Where(item => !item.Option.Disabled)
            .Select(item => new MediaSectionNavigationItem(
                Label: BuildLabel(preset, item.Tab, item.Option),
                Route: BuildRoute(preset, item.Tab, item.Option),
                Icon: item.Option.Icon,
                Exact: true))
            .DistinctBy(item => item.Route, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new MediaSectionNavigationGroup(label, items);
    }

    public static string BuildRoute(
        LibraryBrowsePreset preset,
        BrowseTabPreset tab,
        BrowseGroupingOption option)
    {
        var route = $"{preset.RouteBase}/{tab.Id}";
        return string.Equals(option.Value, tab.DefaultGrouping, StringComparison.OrdinalIgnoreCase)
            ? route
            : $"{route}?browse={Uri.EscapeDataString(option.Value)}";
    }

    private static string BuildLabel(
        LibraryBrowsePreset preset,
        BrowseTabPreset tab,
        BrowseGroupingOption option)
    {
        var duplicate = preset.Tabs
            .SelectMany(candidate => candidate.GroupingOptions)
            .Count(candidate => string.Equals(candidate.Label, option.Label, StringComparison.OrdinalIgnoreCase)) > 1;

        return duplicate ? $"{tab.Label}: {option.Label}" : option.Label;
    }
}
