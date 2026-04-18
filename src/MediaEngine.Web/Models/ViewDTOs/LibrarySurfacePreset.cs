namespace MediaEngine.Web.Models.ViewDTOs;

public sealed record LibrarySurfacePreset
{
    public required string RouteBase { get; init; }
    public required string Title { get; init; }
    public IReadOnlyList<string> VisibleMediaTabs { get; init; } = [];
    public string? DefaultMediaTab { get; init; }
    public IReadOnlyDictionary<string, string> DefaultViewModes { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public bool HidePrimaryTabs { get; init; } = true;
}
