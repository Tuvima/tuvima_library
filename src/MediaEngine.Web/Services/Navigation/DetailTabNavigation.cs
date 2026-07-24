using MediaEngine.Contracts.Details;

namespace MediaEngine.Web.Services.Navigation;

public sealed record DetailTabRouteResolution(
    string ActiveTab,
    bool HasRequestedTab,
    bool IsRequestedTabValid,
    bool ShouldRedirect);

public static class DetailTabNavigation
{
    public static DetailTabRouteResolution Resolve(DetailPageViewModel model, string? requestedTab)
    {
        var visibleTabs = GetVisibleTabs(model).ToList();
        var fallback = visibleTabs.FirstOrDefault()?.Key ?? "overview";
        var hasRequested = !string.IsNullOrWhiteSpace(requestedTab);

        if (!hasRequested)
        {
            return new DetailTabRouteResolution(fallback, false, true, false);
        }

        var requested = Normalize(requestedTab!);
        var match = visibleTabs.FirstOrDefault(tab => string.Equals(Normalize(tab.Key), requested, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return new DetailTabRouteResolution(fallback, true, false, true);
        }

        var isCanonical = string.Equals(requestedTab, match.Key, StringComparison.Ordinal);
        return new DetailTabRouteResolution(match.Key, true, true, !isCanonical);
    }

    public static IReadOnlyList<DetailTab> GetVisibleTabs(DetailPageViewModel model) =>
        model.Tabs
            .Where(tab => !tab.IsAdminOnly || model.IsAdminView)
            .Where(tab => HasRenderableContent(model, tab.Key))
            .ToList();

    private static string Normalize(string value)
    {
        var chars = value
            .Trim()
            .Where(char.IsLetterOrDigit)
            .ToArray();

        return new string(chars).ToLowerInvariant();
    }

    private static bool HasRenderableContent(DetailPageViewModel model, string key)
    {
        return key switch
        {
            "cast" or "people" or "credits" or "contributors" =>
                model.ContributorGroups.Count > 0 || model.CharacterGroups.Count > 0,
            "characters" or "portrayals" =>
                model.CharacterGroups.Count > 0,
            "series" => model.SequencePlacement is not null,
            "related" => model.MediaGroups.Any(IsRecommendationGroup),
            _ => true,
        };
    }

    private static bool IsRecommendationGroup(MediaGroupingViewModel group)
    {
        var key = group.Key ?? string.Empty;
        var title = group.Title ?? string.Empty;
        return key.Equals("related", StringComparison.OrdinalIgnoreCase)
            || key.Equals("recommendations", StringComparison.OrdinalIgnoreCase)
            || key.Equals("more-like-this", StringComparison.OrdinalIgnoreCase)
            || title.Contains("more like", StringComparison.OrdinalIgnoreCase)
            || title.Contains("recommend", StringComparison.OrdinalIgnoreCase)
            || title.Contains("similar", StringComparison.OrdinalIgnoreCase);
    }
}
