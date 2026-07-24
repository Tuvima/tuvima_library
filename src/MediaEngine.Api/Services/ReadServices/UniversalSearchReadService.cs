using Dapper;
using MediaEngine.Api.Models;
using MediaEngine.Contracts.Display;
using MediaEngine.Storage;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.ReadServices;

public interface IUniversalSearchReadService
{
    Task<UniversalSearchResponseDto> SearchAsync(string? query, int limit, CancellationToken ct);
}

public sealed class UniversalSearchReadService(
    IDatabaseConnection db,
    ICollectionSearchReadService workSearch) : IUniversalSearchReadService
{
    public async Task<UniversalSearchResponseDto> SearchAsync(string? query, int limit, CancellationToken ct)
    {
        var trimmed = query?.Trim() ?? string.Empty;
        if (trimmed.Length < 2)
        {
            return new UniversalSearchResponseDto(trimmed, null, [], 0);
        }

        var take = Math.Clamp(limit, 6, 80);
        var worksTask = workSearch.SearchAsync(trimmed, ct);
        var peopleTask = SearchPeopleAsync(trimmed, Math.Min(8, take), ct);
        var collectionsTask = SearchCollectionsAsync(trimmed, Math.Min(10, take), ct);
        await Task.WhenAll(worksTask, peopleTask, collectionsTask).ConfigureAwait(false);

        var results = new List<UniversalSearchResultDto>();
        results.AddRange((await worksTask.ConfigureAwait(false)).Select(result => FromWork(result, trimmed)));
        results.AddRange(await peopleTask.ConfigureAwait(false));
        results.AddRange(await collectionsTask.ConfigureAwait(false));

        results = results
            .DistinctBy(result => $"{result.EntityType}:{result.Id}", StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(result => result.Relevance)
            .ThenBy(result => result.Title, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .ToList();

        var sections = BuildSections(results, trimmed);
        return new UniversalSearchResponseDto(trimmed, results.FirstOrDefault(), sections, results.Count);
    }

    private async Task<IReadOnlyList<UniversalSearchResultDto>> SearchPeopleAsync(
        string query,
        int limit,
        CancellationToken ct)
    {
        using var conn = db.CreateConnection();
        var rows = (await conn.QueryAsync<PersonSearchRow>(new CommandDefinition(
            """
            SELECT p.id AS Id,
                   p.name AS Name,
                   p.biography AS Biography,
                   p.occupation AS Occupation,
                   p.local_headshot_path AS LocalHeadshotPath,
                   GROUP_CONCAT(DISTINCT pr.role) AS Roles
            FROM persons p
            LEFT JOIN person_roles pr ON pr.person_id = p.id
            WHERE p.name LIKE @Like COLLATE NOCASE
            GROUP BY p.id, p.name, p.biography, p.occupation, p.local_headshot_path
            ORDER BY CASE WHEN lower(p.name) = lower(@Query) THEN 0
                          WHEN lower(p.name) LIKE lower(@Prefix) THEN 1
                          ELSE 2 END,
                     p.name COLLATE NOCASE
            LIMIT @Limit
            """,
            new { Query = query, Prefix = $"{query}%", Like = $"%{query}%", Limit = limit },
            cancellationToken: ct)).ConfigureAwait(false)).AsList();

        return rows.Select(row =>
        {
            var roles = Split(row.Roles);
            var subtitle = roles.Count > 0 ? string.Join(", ", roles.Take(3)) : row.Occupation;
            return new UniversalSearchResultDto(
                row.Id,
                "person",
                null,
                row.Name,
                subtitle,
                null,
                null,
                $"/persons/{row.Id:D}/headshot",
                row.Biography,
                $"/details/person/{row.Id:D}",
                "View Person",
                MatchReason(row.Name, query, "person name"),
                Score(row.Name, query, 0.98))
            {
                Facts = roles,
            };
        }).ToList();
    }

    private async Task<IReadOnlyList<UniversalSearchResultDto>> SearchCollectionsAsync(
        string query,
        int limit,
        CancellationToken ct)
    {
        using var conn = db.CreateConnection();
        var rows = (await conn.QueryAsync<CollectionSearchRow>(new CommandDefinition(
            """
            SELECT c.id AS Id,
                   COALESCE(c.display_name, title.value, 'Untitled collection') AS Name,
                   c.collection_type AS CollectionType,
                   c.description AS Description,
                   c.square_artwork_path AS SquareArtworkPath,
                   COUNT(DISTINCT ci.work_id) AS ItemCount
            FROM collections c
            LEFT JOIN canonical_values title ON title.entity_id = c.id AND title.key = 'title'
            LEFT JOIN collection_items ci ON ci.collection_id = c.id
            WHERE c.is_enabled = 1
              AND (COALESCE(c.display_name, title.value, '') LIKE @Like COLLATE NOCASE
                   OR COALESCE(c.description, '') LIKE @Like COLLATE NOCASE)
            GROUP BY c.id, c.display_name, title.value, c.collection_type, c.description, c.square_artwork_path
            ORDER BY CASE WHEN lower(COALESCE(c.display_name, title.value, '')) = lower(@Query) THEN 0
                          WHEN lower(COALESCE(c.display_name, title.value, '')) LIKE lower(@Prefix) THEN 1
                          ELSE 2 END,
                     ItemCount DESC,
                     Name COLLATE NOCASE
            LIMIT @Limit
            """,
            new { Query = query, Prefix = $"{query}%", Like = $"%{query}%", Limit = limit },
            cancellationToken: ct)).ConfigureAwait(false)).AsList();

        return rows.Select(row =>
        {
            var entityType = CollectionEntityType(row.CollectionType);
            var isPlaylist = entityType == "playlist";
            var route = isPlaylist
                ? $"/listen/music/playlists/{row.Id:D}"
                : $"/collection/{row.Id:D}";
            var typeLabel = isPlaylist ? "Playlist" : CollectionTypeLabel(row.CollectionType);
            return new UniversalSearchResultDto(
                row.Id,
                entityType,
                null,
                row.Name,
                $"{row.ItemCount} {(row.ItemCount == 1 ? "item" : "items")}",
                null,
                null,
                string.IsNullOrWhiteSpace(row.SquareArtworkPath) ? null : $"/collections/{row.Id:D}/square-artwork",
                row.Description,
                route,
                isPlaylist ? "Open Playlist" : entityType == "series" ? "View Series" : "Open Collection",
                MatchReason(row.Name, query, typeLabel.ToLowerInvariant()),
                Score(row.Name, query, isPlaylist ? 0.94 : 0.96))
            {
                Facts = [typeLabel, $"{row.ItemCount} owned"],
            };
        }).ToList();
    }

    private static UniversalSearchResultDto FromWork(SearchResultDto result, string query)
    {
        var mediaType = NormalizeMediaType(result.MediaType);
        var creator = SameText(result.Author, result.Title) ? null : result.Author;
        var subtitle = FirstDifferent(result.Title, creator, result.ShowName, result.Series, result.CollectionDisplayName);
        var route = WorkRoute(result, mediaType);
        var matchSource = ResolveWorkMatchSource(result, query);
        var relevance = matchSource switch
        {
            "title" => Score(result.Title, query, 1.0),
            "creator" => Score(result.Author, query, 0.92),
            "series" => Score(FirstNonBlank(result.Series, result.ShowName, result.CollectionDisplayName), query, 0.90),
            _ => 0.72,
        };

        return new UniversalSearchResultDto(
            result.WorkId,
            WorkEntityType(mediaType),
            mediaType,
            result.Title,
            subtitle,
            creator,
            result.Year,
            result.CoverUrl,
            result.Description,
            route,
            PrimaryActionLabel(mediaType),
            $"Matched {matchSource}",
            relevance)
        {
            Facts = new[] { result.Rating, result.Series }
                .Where(value => !string.IsNullOrWhiteSpace(value) && !SameText(value, result.Title))
                .Cast<string>()
                .ToList(),
        };
    }

    private static IReadOnlyList<UniversalSearchSectionDto> BuildSections(
        IReadOnlyList<UniversalSearchResultDto> results,
        string query)
    {
        var definitions = new[]
        {
            new SectionDefinition("books", "Books & Comics", result => result.MediaType is "Book" or "Comic", $"/read/books?q={Uri.EscapeDataString(query)}"),
            new SectionDefinition("watch", "Movies & TV", result => result.MediaType is "Movie" or "TV", $"/watch/movies?q={Uri.EscapeDataString(query)}"),
            new SectionDefinition("music", "Music", result => result.MediaType == "Music", $"/listen/music?q={Uri.EscapeDataString(query)}"),
            new SectionDefinition("audiobooks", "Audiobooks", result => result.MediaType == "Audiobook", $"/listen/audiobooks?q={Uri.EscapeDataString(query)}"),
            new SectionDefinition("people", "People", result => result.EntityType == "person", $"/search?q={Uri.EscapeDataString(query)}&type=person"),
            new SectionDefinition("series-collections", "Series & Collections", result => result.EntityType is "series" or "collection" or "playlist", $"/search?q={Uri.EscapeDataString(query)}&type=group"),
        };

        return definitions
            .Select((definition, index) =>
            {
                var matches = results.Where(definition.Predicate).ToList();
                return new
                {
                    Definition = definition,
                    Matches = matches,
                    Index = index,
                    MaxScore = matches.Count == 0 ? 0 : matches.Max(result => result.Relevance),
                };
            })
            .Where(item => item.Matches.Count > 0)
            .OrderByDescending(item => item.MaxScore)
            .ThenBy(item => item.Index)
            .Select(item => new UniversalSearchSectionDto(
                item.Definition.Key,
                item.Definition.Title,
                item.Matches.Take(6).ToList(),
                item.Matches.Count,
                item.Definition.SeeAllRoute))
            .ToList();
    }

    private static string WorkRoute(SearchResultDto result, string mediaType) => mediaType switch
    {
        "Movie" => result.CollectionId.HasValue
            ? $"/watch/movie/{result.WorkId:D}?collectionId={result.CollectionId.Value:D}"
            : $"/watch/movie/{result.WorkId:D}",
        "TV" => result.CollectionId.HasValue ? $"/watch/tv/show/{result.CollectionId.Value:D}" : "/watch/tv",
        "Music" => $"/details/musictrack/{result.WorkId:D}?context=listen",
        "Audiobook" => $"/details/audiobook/{result.WorkId:D}?context=listen",
        _ => $"/book/{result.WorkId:D}?mode=read",
    };

    private static string NormalizeMediaType(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Contains("tv") || normalized.Contains("television")) return "TV";
        if (normalized.Contains("movie") || normalized.Contains("video")) return "Movie";
        if (normalized.Contains("audiobook") || normalized.Contains("m4b")) return "Audiobook";
        if (normalized.Contains("music") || normalized == "audio") return "Music";
        if (normalized.Contains("comic")) return "Comic";
        return "Book";
    }

    private static string WorkEntityType(string mediaType) => mediaType switch
    {
        "TV" => "tv-show",
        "Movie" => "movie",
        "Music" => "album",
        "Audiobook" => "audiobook",
        "Comic" => "comic",
        _ => "book",
    };

    private static string PrimaryActionLabel(string mediaType) => mediaType switch
    {
        "TV" or "Movie" => "Watch",
        "Music" => "Play",
        "Audiobook" => "Listen",
        _ => "Read",
    };

    private static string ResolveWorkMatchSource(SearchResultDto result, string query)
    {
        if (Contains(result.Title, query)) return "title";
        if (Contains(result.Author, query)) return "creator";
        if (Contains(result.Series, query) || Contains(result.ShowName, query) || Contains(result.CollectionDisplayName, query)) return "series";
        return "metadata";
    }

    private static string CollectionEntityType(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Contains("playlist") || normalized.Contains("mix")) return "playlist";
        if (normalized.Contains("series") || normalized.Contains("album") || normalized.Contains("show")) return "series";
        return "collection";
    }

    private static string CollectionTypeLabel(string? value) => string.IsNullOrWhiteSpace(value) ? "Collection" : value!;

    private static string MatchReason(string? candidate, string query, string field) =>
        string.Equals(candidate, query, StringComparison.OrdinalIgnoreCase)
            ? $"Exact {field} match"
            : candidate?.StartsWith(query, StringComparison.OrdinalIgnoreCase) == true
                ? $"{field} prefix match"
                : $"Matched {field}";

    private static double Score(string? candidate, string query, double ceiling)
    {
        if (string.Equals(candidate, query, StringComparison.OrdinalIgnoreCase)) return ceiling;
        if (candidate?.StartsWith(query, StringComparison.OrdinalIgnoreCase) == true) return Math.Max(0, ceiling - 0.05);
        return Math.Max(0, ceiling - 0.18);
    }

    private static bool Contains(string? value, string query) =>
        !string.IsNullOrWhiteSpace(value) && value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> Split(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? FirstDifferent(string title, params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) && !SameText(value, title));

    private static bool SameText(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private sealed class PersonSearchRow
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? Biography { get; init; }
        public string? Occupation { get; init; }
        public string? LocalHeadshotPath { get; init; }
        public string? Roles { get; init; }
    }

    private sealed class CollectionSearchRow
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? CollectionType { get; init; }
        public string? Description { get; init; }
        public string? SquareArtworkPath { get; init; }
        public int ItemCount { get; init; }
    }

    private sealed record SectionDefinition(
        string Key,
        string Title,
        Func<UniversalSearchResultDto, bool> Predicate,
        string SeeAllRoute);
}
