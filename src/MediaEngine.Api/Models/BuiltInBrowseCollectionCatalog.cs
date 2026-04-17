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
            "Comics by Series",
            "Comics grouped by series",
            "AutoStories",
            "System",
            "query",
            [new CollectionRulePredicate { Field = "media_type", Op = "eq", Value = "Comics" }],
            GroupByField: "series",
            SortField: "title",
            SortDirection: "asc"),
    ];

    public static readonly IReadOnlySet<string> LegacySampleSmartNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "By Genre: Science Fiction",
        "By Genre: Mystery",
        "By Genre: Biography",
        "By Vibe: Atmospheric",
        "By Vibe: Cozy",
        "By Vibe: Cerebral",
        "By Author: Frank Herbert",
        "By Director: Denis Villeneuve",
        "By Narrator: Steven Pacey",
        "By Decade: 1980s",
        "By Decade: 2010s",
        "Recently Added",
        "Highest Rated",
        "Unrated",
    };

    public static readonly IReadOnlySet<string> LegacySamplePlaylistNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Workout Mix",
        "Movie Marathon",
        "Commute Rotation",
    };

    public static readonly IReadOnlySet<string> LegacyGeneratedBrowseNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "All Books",
        "All Audiobooks",
        "All Comics",
        "All Movies",
        "All TV",
        "All TV Shows",
        "All Music",
        "All Songs",
        "All Albums",
        "All Artists",
        "By Artist",
        "By Album",
        "By Show",
        "By Series",
    };

    public static readonly IReadOnlySet<string> LegacyGeneratedNames = new HashSet<string>(
        DynamicBrowseViews.Select(view => view.Name)
            .Concat(LegacySampleSmartNames)
            .Concat(LegacySamplePlaylistNames)
            .Concat(LegacyGeneratedBrowseNames),
        StringComparer.OrdinalIgnoreCase);

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
