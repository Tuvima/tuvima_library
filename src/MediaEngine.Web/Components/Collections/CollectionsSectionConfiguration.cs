using MediaEngine.Web.Models.ViewDTOs;
using MudBlazor;

namespace MediaEngine.Web.Components.Collections;

internal static class CollectionsSectionConfiguration
{
    public const string Overview = "overview";
    public const string Automatic = "automatic";
    public const string Curated = "curated";
    public const string Shelves = "shelves";
    public const string People = "people";

    public static IReadOnlyList<MediaHubModeViewModel> Modes { get; } =
    [
        new(Overview, "Discovery", "/collections"),
        new(Automatic, "Automatic", "/collections/automatic"),
        new(Curated, "Curated", "/collections/curated"),
        new(Shelves, "Shelves", "/collections/shelves"),
        new(People, "People", "/collections/people"),
    ];

    public static IReadOnlyList<MediaSectionNavigationGroup> BuildNavigation(
        int automaticCount,
        int curatedCount,
        int shelfCount,
        string? peopleCount) =>
    [
        new("Collections",
        [
            new("Discovery", "/collections", Icons.Material.Outlined.Explore, Exact: true),
            new("Automatic", "/collections/automatic", Icons.Material.Outlined.AutoAwesome, Count(automaticCount)),
            new("Curated", "/collections/curated", Icons.Material.Outlined.CollectionsBookmark, Count(curatedCount)),
        ]),
        new("Browse library",
        [
            new("Shelves", "/collections/shelves", Icons.Material.Outlined.ViewCarousel, Count(shelfCount)),
            new("People", "/collections/people", Icons.Material.Outlined.People, peopleCount),
        ]),
        new("Shortcuts",
        [
            new("Cross-media", "/collections/automatic?lane=CrossMedia", Icons.Material.Outlined.Hub, Exact: true),
            new("Published", "/collections/curated?status=published", Icons.Material.Outlined.Public, Exact: true),
            new("Recently Updated", "/collections/curated?sort=recent", Icons.Material.Outlined.Schedule, Exact: true),
        ]),
    ];

    public static string NormalizeSection(string? section) => section?.Trim().ToLowerInvariant() switch
    {
        Automatic => Automatic,
        Curated => Curated,
        Shelves => Shelves,
        People => People,
        _ => Overview,
    };

    private static string? Count(int value) =>
        value > 0 ? value.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
}
