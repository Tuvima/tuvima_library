namespace MediaEngine.Web.Components.MediaEditor;

/// <summary>
/// Owns editor tab transitions so file-scope navigation and the return tab cannot
/// drift apart across the normal, review, and batch editor entry points.
/// </summary>
public sealed class MediaEditorTabState
{
    public string ActiveTab { get; private set; } = "details";
    public string LastNonFileTab { get; private set; } = "details";

    public void Initialize(string? tabId)
    {
        ActiveTab = Normalize(tabId);
        LastNonFileTab = string.Equals(ActiveTab, "file", StringComparison.OrdinalIgnoreCase)
            ? "details"
            : ActiveTab;
    }

    public void Activate(string? tabId)
    {
        ActiveTab = Normalize(tabId);
        if (!string.Equals(ActiveTab, "file", StringComparison.OrdinalIgnoreCase))
            LastNonFileTab = ActiveTab;
    }

    public void ActivateFile() => ActiveTab = "file";

    public void RememberCurrentNonFile()
    {
        if (!string.Equals(ActiveTab, "file", StringComparison.OrdinalIgnoreCase))
            LastNonFileTab = ActiveTab;
    }

    public void EnsureVisible(Func<string, bool> isVisible, IEnumerable<string> visibleTabs)
    {
        if (isVisible(ActiveTab))
            return;

        if (!string.IsNullOrWhiteSpace(LastNonFileTab) && isVisible(LastNonFileTab))
        {
            Activate(LastNonFileTab);
            return;
        }

        Activate(visibleTabs.FirstOrDefault() ?? "details");
    }

    public static string Normalize(string? tabId) =>
        (tabId ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "identity" => "links",
            "universe" => "links",
            "id" => "links",
            "inspector" => "file",
            "" => "details",
            var normalized => normalized,
        };
}
