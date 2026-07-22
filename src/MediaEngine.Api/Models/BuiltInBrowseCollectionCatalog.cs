using System.Security.Cryptography;
using System.Text;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Models;
using MediaEngine.Storage;

namespace MediaEngine.Api.Models;

public sealed record BuiltInBrowseCollectionDefinition(
    string Name,
    string Description,
    string Icon,
    string CollectionType,
    string Resolution,
    IReadOnlyList<CollectionRulePredicate> Rules,
    string? GroupByField = null,
    string MatchMode = "all",
    string? SortField = null,
    string SortDirection = "desc")
{
    public Collection ToCollection() => new()
    {
        Id = CreateDeterministicGuid(Name),
        DisplayName = Name,
        Description = Description,
        IconName = Icon,
        CollectionType = CollectionType,
        Scope = "library",
        IsEnabled = true,
        Resolution = Resolution,
        RuleJson = System.Text.Json.JsonSerializer.Serialize(Rules),
        RuleHash = CollectionRuleEvaluator.ComputeRuleHash(Rules),
        GroupByField = GroupByField,
        MatchMode = MatchMode,
        SortField = SortField,
        SortDirection = SortDirection,
        LiveUpdating = true,
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    private static Guid CreateDeterministicGuid(string value)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(bytes);
    }
}

public static class BuiltInBrowseCollectionCatalog
{
    public static readonly IReadOnlyList<BuiltInBrowseCollectionDefinition> DynamicBrowseViews =
    [
        new(
            "Recently Added",
            "Items added in the last 30 days",
            "NewReleases",
            "System",
            "query",
            [new CollectionRulePredicate { Field = "added_within_days", Op = "lte", Value = "30" }],
            SortField: "newest"),
        new(
            "All Songs",
            "Every song in your library",
            "QueueMusic",
            "System",
            "query",
            [new CollectionRulePredicate { Field = "media_type", Op = "eq", Value = "Music" }],
            SortField: "title",
            SortDirection: "asc"),
        new(
            "TV by Show",
            "TV episodes grouped by show",
            "LiveTv",
            "System",
            "query",
            [new CollectionRulePredicate { Field = "media_type", Op = "eq", Value = "TV" }],
            GroupByField: "show_name",
            SortField: "newest"),
        new(
            "Music by Artist",
            "Music grouped by artist",
            "MusicNote",
            "System",
            "query",
            [new CollectionRulePredicate { Field = "media_type", Op = "eq", Value = "Music" }],
            GroupByField: "artist",
            SortField: "title",
            SortDirection: "asc"),
        new(
            "Music by Album",
            "Music grouped by album",
            "Album",
            "System",
            "query",
            [new CollectionRulePredicate { Field = "media_type", Op = "eq", Value = "Music" }],
            GroupByField: "album",
            SortField: "title",
            SortDirection: "asc"),
        new(
            "Books by Series",
            "Books grouped by series",
            "LibraryBooks",
            "System",
            "query",
            [new CollectionRulePredicate { Field = "media_type", Op = "eq", Value = "Books" }],
            GroupByField: "series",
            SortField: "title",
            SortDirection: "asc"),
        new(
            "Books by Author",
            "Books grouped by author after library filters are applied",
            "PersonOutline",
            "System",
            "query",
            [new CollectionRulePredicate { Field = "media_type", Op = "eq", Value = "Books" }],
            GroupByField: "author",
            SortField: "title",
            SortDirection: "asc"),
        new(
            "Audiobooks by Series",
            "Audiobooks grouped by series",
            "Headphones",
            "System",
            "query",
            [new CollectionRulePredicate { Field = "media_type", Op = "eq", Value = "Audiobooks" }],
            GroupByField: "series",
            SortField: "title",
            SortDirection: "asc"),
        new(
            "Audiobooks by Author",
            "Audiobooks grouped by author",
            "PersonOutline",
            "System",
            "query",
            [new CollectionRulePredicate { Field = "media_type", Op = "eq", Value = "Audiobooks" }],
            GroupByField: "author",
            SortField: "title",
            SortDirection: "asc"),
        new(
            "Audiobooks by Narrator",
            "Audiobooks grouped by narrator",
            "RecordVoiceOver",
            "System",
            "query",
            [new CollectionRulePredicate { Field = "media_type", Op = "eq", Value = "Audiobooks" }],
            GroupByField: "narrator",
            SortField: "title",
            SortDirection: "asc"),
        new(
            "Comics by Series",
            "Comics grouped by series",
            "AutoStories",
            "System",
            "query",
            [new CollectionRulePredicate { Field = "media_type", Op = "eq", Value = "Comics" }],
            GroupByField: "series",
            SortField: "title",
            SortDirection: "asc"),
        new(
            "Comics by Creator",
            "Comics grouped by creator",
            "PeopleOutline",
            "System",
            "query",
            [new CollectionRulePredicate { Field = "media_type", Op = "eq", Value = "Comics" }],
            GroupByField: "author",
            SortField: "title",
            SortDirection: "asc"),
        new(
            "Comics by Publisher",
            "Comics grouped by publisher",
            "Business",
            "System",
            "query",
            [new CollectionRulePredicate { Field = "media_type", Op = "eq", Value = "Comics" }],
            GroupByField: "publisher",
            SortField: "title",
            SortDirection: "asc"),
        new(
            "Movies by Director",
            "Movies grouped by director",
            "MovieFilter",
            "System",
            "query",
            [new CollectionRulePredicate { Field = "media_type", Op = "eq", Value = "Movies" }],
            GroupByField: "director",
            SortField: "title",
            SortDirection: "asc"),
        new(
            "TV by Network",
            "TV shows grouped by network or service",
            "Podcasts",
            "System",
            "query",
            [new CollectionRulePredicate { Field = "media_type", Op = "eq", Value = "TV" }],
            GroupByField: "network",
            SortField: "title",
            SortDirection: "asc"),
    ];

    public static IEnumerable<BuiltInBrowseCollectionDefinition> GetSystemViewDefinitions(string? mediaType, string? groupField)
    {
        var query = DynamicBrowseViews.Where(view => !string.IsNullOrWhiteSpace(view.GroupByField));

        if (!string.IsNullOrWhiteSpace(mediaType))
        {
            query = query.Where(view =>
                view.Rules.Any(rule =>
                    string.Equals(rule.Field, "media_type", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(rule.Value, mediaType, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(groupField))
            query = query.Where(view => string.Equals(view.GroupByField, groupField, StringComparison.OrdinalIgnoreCase));

        return query;
    }

    public static BuiltInBrowseCollectionDefinition? FindByName(string? name) =>
        DynamicBrowseViews.FirstOrDefault(view =>
            string.Equals(view.Name, name, StringComparison.OrdinalIgnoreCase));
}
