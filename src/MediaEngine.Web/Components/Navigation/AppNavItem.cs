namespace MediaEngine.Web.Components.Navigation;

public sealed record AppNavItem
{
    public required string Key { get; init; }

    public required string Label { get; init; }

    public string? Icon { get; init; }

    public string? Href { get; init; }

    public string? Badge { get; init; }

    public string? Description { get; init; }

    public bool Disabled { get; init; }

    public bool Attention { get; init; }
}
