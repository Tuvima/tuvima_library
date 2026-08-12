using System.Text.Json;
using Dapper;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.ReadServices;

public sealed class EditorSuggestionReadService
{
    private readonly IDatabaseConnection _db;

    public EditorSuggestionReadService(IDatabaseConnection db) => _db = db;

    public IReadOnlyList<string> GetValues(string field, Guid? profileId, int limit, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var take = Math.Clamp(limit, 1, 500);
        using var connection = _db.CreateConnection();

        IEnumerable<string> values = field.Trim().ToLowerInvariant() switch
        {
            "genre" => ReadGenres(connection, ct),
            "tag" or "tags" or "custom_tags" => ReadTags(connection, profileId, ct),
            _ => [],
        };

        return values
            .SelectMany(SplitValues)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .ToList();
    }

    private static IEnumerable<string> ReadGenres(System.Data.IDbConnection connection, CancellationToken ct)
    {
        var canonical = connection.Query<string>(new CommandDefinition(
            """
            SELECT value FROM canonical_value_arrays WHERE key = 'genre' AND NULLIF(TRIM(value), '') IS NOT NULL
            UNION ALL
            SELECT value FROM canonical_values WHERE key = 'genre' AND NULLIF(TRIM(value), '') IS NOT NULL;
            """,
            cancellationToken: ct));
        var overrides = connection.Query<string?>(new CommandDefinition(
            "SELECT json_extract(display_overrides_json, '$.genre') FROM works WHERE json_valid(display_overrides_json);",
            cancellationToken: ct));
        return canonical.Concat(overrides.OfType<string>().Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static IEnumerable<string> ReadTags(System.Data.IDbConnection connection, Guid? profileId, CancellationToken ct)
    {
        if (!profileId.HasValue)
            return [];

        return connection.Query<string?>(new CommandDefinition(
                """
                SELECT local_tags_json FROM profile_work_preferences
                WHERE profile_id = @profileId AND local_tags_json IS NOT NULL
                UNION ALL
                SELECT local_tags_json FROM profile_person_preferences
                WHERE profile_id = @profileId AND local_tags_json IS NOT NULL;
                """,
                new { profileId = profileId.Value },
                cancellationToken: ct))
            .SelectMany(ParseJsonArray);
    }

    private static IEnumerable<string> ParseJsonArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IEnumerable<string> SplitValues(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
