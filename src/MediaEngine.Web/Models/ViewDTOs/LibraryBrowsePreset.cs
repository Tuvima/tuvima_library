namespace MediaEngine.Web.Models.ViewDTOs;

public enum LibraryLayoutMode
{
    Card,
    List,
}

public enum BrowseHeroVariant
{
    Read,
    Watch,
    Listen,
}

public sealed record BrowseGroupingOption(string Value, string Label, string Icon);

public sealed record BrowseTabPreset
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string MediaType { get; init; }
    public IReadOnlyList<BrowseGroupingOption> GroupingOptions { get; init; } = [];
    public string DefaultGrouping { get; init; } = "all";
    public LibraryLayoutMode DefaultLayout { get; init; } = LibraryLayoutMode.Card;
}

public sealed record LibraryBrowsePreset
{
    public required string RouteBase { get; init; }
    public required string Title { get; init; }
    public required BrowseHeroVariant HeroVariant { get; init; }
    public IReadOnlyList<BrowseTabPreset> Tabs { get; init; } = [];
}

public sealed record BrowseHeroViewModel
{
    public required BrowseHeroVariant Variant { get; init; }
    public required string Eyebrow { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public string? Description { get; init; }
    public string? MetaLine { get; init; }
    public string? SupportingLine { get; init; }
    public string? CoverUrl { get; init; }
    public string? BackgroundUrl { get; init; }
    public double? ProgressPct { get; init; }
    public string PrimaryActionLabel { get; init; } = "Open";
    public string? SecondaryActionLabel { get; init; }
    public string PrimaryNavigationUrl { get; init; } = "/";
    public string? SecondaryNavigationUrl { get; init; }
}
