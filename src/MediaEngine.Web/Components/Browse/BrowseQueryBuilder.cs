using MediaEngine.Web.Models.ViewDTOs;

namespace MediaEngine.Web.Components.Browse;

public static class BrowseQueryBuilder
{
    public static BrowseState Read(
        LibraryBrowsePreset preset,
        string? requestedTab,
        string absoluteUri)
    {
        var normalizedTab = string.IsNullOrWhiteSpace(requestedTab) ? null : requestedTab.Trim().ToLowerInvariant();
        var fallbackTab = preset.Tabs[0];
        var resolvedTab = preset.Tabs.FirstOrDefault(tab =>
            string.Equals(tab.Id, normalizedTab, StringComparison.OrdinalIgnoreCase)) ?? fallbackTab;

        var query = System.Web.HttpUtility.ParseQueryString(new Uri(absoluteUri).Query);
        var grouping = ResolveGrouping(query["grouping"], resolvedTab);
        var layout = ResolveLayout(query["layout"], resolvedTab, grouping);
        var sortBy = ResolveSort(query["sort"], resolvedTab.Id, grouping);

        return new BrowseState(
            resolvedTab.Id,
            grouping,
            query["search"] ?? string.Empty,
            sortBy,
            layout,
            ParseList(query["genres"]),
            query["creator"] ?? string.Empty,
            query["status"] ?? string.Empty,
            query["year"] ?? string.Empty);
    }

    public static string ResolveGrouping(string? requestedGrouping, BrowseTabPreset tab)
    {
        if (string.IsNullOrWhiteSpace(requestedGrouping))
        {
            return tab.DefaultGrouping;
        }

        return tab.GroupingOptions.Any(option => string.Equals(option.Value, requestedGrouping, StringComparison.OrdinalIgnoreCase))
            ? requestedGrouping
            : tab.DefaultGrouping;
    }

    public static LibraryLayoutMode ResolveLayout(string? requestedLayout, BrowseTabPreset tab, string grouping)
    {
        if (grouping == "tracks")
        {
            return LibraryLayoutMode.List;
        }

        return Enum.TryParse<LibraryLayoutMode>(requestedLayout, ignoreCase: true, out var parsed)
            ? parsed
            : tab.DefaultLayout;
    }

    public static string ResolveSort(string? requestedSort, string activeTabId, string grouping)
    {
        var allowed = GetSortOptions(activeTabId, grouping).Select(option => option.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(requestedSort) && allowed.Contains(requestedSort))
        {
            return requestedSort;
        }

        return IsContainerGrouping(grouping) ? "featured" : "newest";
    }

    public static IReadOnlyList<(string Value, string Label)> GetSortOptions(string activeTabId, string grouping) =>
        IsContainerGrouping(grouping)
            ? [("featured", "Featured"), ("title", "A-Z"), ("newest", "Newest")]
            : [("newest", "Newest"), ("title", "A-Z"), ("oldest", "Oldest"), ("creator", "Creator"), ("year", "Year")];

    public static bool IsContainerGrouping(string grouping) =>
        grouping is "series" or "shows" or "albums" or "artists";

    public static string? GetSystemViewGroupField(string activeTabId, string grouping) => (activeTabId, grouping) switch
    {
        ("music", "artists") => "artist",
        ("music", "albums") => "album",
        ("books", "series") => "series",
        ("audiobooks", "series") => "series",
        _ => null,
    };

    private static IReadOnlyList<string> ParseList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

}
