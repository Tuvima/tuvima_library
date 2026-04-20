namespace MediaEngine.Web.Components.Shared;

public readonly record struct AppIconDescriptor(string Family, string Name)
{
    public string Path => $"/icons/fontawesome/{Family}/{Name}.svg";
}

public static class AppIconRegistry
{
    private static readonly IReadOnlyDictionary<string, AppIconDescriptor> Aliases =
        new Dictionary<string, AppIconDescriptor>(StringComparer.OrdinalIgnoreCase)
        {
            [AppIcons.Search] = Solid("magnifying-glass"),
            [AppIcons.Home] = Solid("house"),
            [AppIcons.Read] = Solid("book-open-reader"),
            [AppIcons.Watch] = Solid("film"),
            [AppIcons.Listen] = Solid("headphones"),
            [AppIcons.Review] = Solid("clipboard-check"),
            [AppIcons.Collections] = Solid("boxes-stacked"),
            [AppIcons.Settings] = Solid("gear"),
            [AppIcons.Overview] = Solid("layer-group"),
            [AppIcons.Library] = Solid("folder-open"),
            [AppIcons.Providers] = Solid("share-nodes"),
            [AppIcons.Intelligence] = Solid("wand-magic-sparkles"),
            [AppIcons.Server] = Solid("server"),
            [AppIcons.ChevronDown] = Solid("chevron-down"),
            [AppIcons.ChevronLeft] = Solid("chevron-left"),
            [AppIcons.ChevronRight] = Solid("chevron-right"),
            [AppIcons.Profile] = Solid("user"),
            [AppIcons.Playback] = Solid("play"),
            [AppIcons.Security] = Solid("shield-halved"),
            [AppIcons.Users] = Solid("users"),
            [AppIcons.Activity] = Solid("timeline"),
            [AppIcons.Maintenance] = Solid("wrench"),
            [AppIcons.Setup] = Solid("list-check"),
            [AppIcons.Warning] = Solid("triangle-exclamation"),
            [AppIcons.Info] = Solid("circle-info"),
            [AppIcons.Close] = Solid("xmark"),
            [AppIcons.Pending] = Solid("clock"),
            [AppIcons.Shopping] = Solid("cart-shopping"),
            [AppIcons.Configure] = Solid("sliders"),
            [AppIcons.Table] = Solid("table-list"),
            [AppIcons.FolderTree] = Solid("folder-tree"),
            [AppIcons.Music] = Solid("music"),
            [AppIcons.Television] = Solid("tv"),
        };

    public static bool TryResolve(string? key, out AppIconDescriptor icon)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            if (Aliases.TryGetValue(Normalize(key), out icon))
            {
                return true;
            }

            if (TryResolveExplicitKey(key, out icon))
            {
                return true;
            }
        }

        icon = default;
        return false;
    }

    private static bool TryResolveExplicitKey(string key, out AppIconDescriptor icon)
    {
        var parts = key.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && string.Equals(parts[0], "fa", StringComparison.OrdinalIgnoreCase))
        {
            icon = Solid(parts[1]);
            return true;
        }

        if (parts.Length == 2 && string.Equals(parts[0], "fab", StringComparison.OrdinalIgnoreCase))
        {
            icon = new AppIconDescriptor("brands", parts[1]);
            return true;
        }

        if (parts.Length == 3 && string.Equals(parts[0], "fa", StringComparison.OrdinalIgnoreCase))
        {
            icon = new AppIconDescriptor(parts[1].ToLowerInvariant(), parts[2].ToLowerInvariant());
            return true;
        }

        icon = default;
        return false;
    }

    private static AppIconDescriptor Solid(string name) => new("solid", name);

    private static string Normalize(string key) =>
        key.Trim().Replace('_', '-').Replace(' ', '-').ToLowerInvariant();
}
