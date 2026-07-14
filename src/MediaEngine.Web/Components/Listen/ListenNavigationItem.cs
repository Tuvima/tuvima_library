namespace MediaEngine.Web.Components.Listen;

public sealed record ListenNavigationItem(
    string Label,
    string Route,
    string Icon,
    string? Meta = null);
