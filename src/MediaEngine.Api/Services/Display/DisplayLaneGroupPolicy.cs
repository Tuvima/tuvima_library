using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Models;

namespace MediaEngine.Api.Services.Display;

public sealed class DisplayLaneGroupPolicy
{
    private static readonly IReadOnlyDictionary<string, DisplayLaneGroupShelfPolicy> Defaults =
        new Dictionary<string, DisplayLaneGroupShelfPolicy>(StringComparer.OrdinalIgnoreCase)
        {
            ["watch"] = new(
                Key: "shows-and-series",
                Title: "Shows & Series",
                Subtitle: "TV shows and film series grouped by title",
                SeeAllRoute: "/watch/tv",
                Enabled: true),
            ["read"] = new(
                Key: "series-and-reading-lists",
                Title: "Series & Reading Lists",
                Subtitle: "Book series, comic runs, and grouped reading",
                SeeAllRoute: "/read/books?grouping=series",
                Enabled: true),
        };

    private readonly IConfigurationLoader? _configLoader;

    public DisplayLaneGroupPolicy(IConfigurationLoader? configLoader = null)
    {
        _configLoader = configLoader;
    }

    public DisplayLaneGroupShelfPolicy GetShelf(string lane)
    {
        var normalized = DisplayMediaRules.NormalizeLane(lane) ?? lane;
        var fallback = Defaults.TryGetValue(normalized, out var defaultPolicy)
            ? defaultPolicy
            : new DisplayLaneGroupShelfPolicy($"{normalized}-groups", "Groups", "Grouped titles from your library", $"/{normalized}", true);

        var settings = LoadPreferences().LaneGroupDisplay.GetValueOrDefault(normalized);
        if (settings is null)
        {
            return fallback;
        }

        return new DisplayLaneGroupShelfPolicy(
            Key: FirstNonBlank(settings.ShelfKey, fallback.Key)!,
            Title: FirstNonBlank(settings.Title, fallback.Title)!,
            Subtitle: FirstNonBlank(settings.Subtitle, fallback.Subtitle)!,
            SeeAllRoute: FirstNonBlank(settings.SeeAllRoute, fallback.SeeAllRoute),
            Enabled: settings.Enabled ?? fallback.Enabled);
    }

    private LibraryPreferencesSettings LoadPreferences()
    {
        if (_configLoader is null)
        {
            return new LibraryPreferencesSettings();
        }

        try
        {
            return _configLoader.LoadConfig<LibraryPreferencesSettings>("ui", "library-preferences")
                   ?? new LibraryPreferencesSettings();
        }
        catch
        {
            // Display grouping defaults are safe to use if an operator is editing UI preferences live.
            return new LibraryPreferencesSettings();
        }
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

public sealed record DisplayLaneGroupShelfPolicy(
    string Key,
    string Title,
    string Subtitle,
    string? SeeAllRoute,
    bool Enabled);
