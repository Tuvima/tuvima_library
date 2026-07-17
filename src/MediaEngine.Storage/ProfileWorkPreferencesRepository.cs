using System.Text.Json;
using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

public sealed class ProfileWorkPreferencesRepository : IProfileWorkPreferencesRepository
{
    private readonly IDatabaseConnection _db;

    public ProfileWorkPreferencesRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    public async Task<ProfileWorkPreferences> GetAsync(
        Guid profileId,
        Guid workId,
        CancellationToken ct = default)
    {
        using var connection = _db.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<PreferenceRow>(new CommandDefinition(
            """
            SELECT profile_id AS ProfileId,
                   work_id AS WorkId,
                   personal_notes AS PersonalNotes,
                   local_tags_json AS LocalTagsJson,
                   is_hidden AS IsHidden,
                   include_in_recommendations AS IncludeInRecommendations,
                   revision AS Revision,
                   updated_at AS UpdatedAt
            FROM profile_work_preferences
            WHERE profile_id = @profileId AND work_id = @workId;
            """,
            new { profileId, workId },
            cancellationToken: ct));

        return row is null ? ProfileWorkPreferences.Empty(profileId, workId) : Map(row);
    }

    public async Task<EditorPreferencesSaveResult> SaveAsync(
        EditorPreferencesSaveCommand command,
        CancellationToken ct = default)
    {
        using var connection = _db.CreateConnection();
        using var transaction = connection.BeginTransaction();

        var workExists = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(1) FROM works WHERE id = @workId;",
            new { workId = command.WorkId }, transaction, cancellationToken: ct)) > 0;
        var profileExists = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(1) FROM profiles WHERE id = @profileId;",
            new { profileId = command.ProfileId }, transaction, cancellationToken: ct)) > 0;

        if (!workExists || !profileExists)
        {
            transaction.Rollback();
            return new EditorPreferencesSaveResult(
                workExists,
                profileExists,
                false,
                ProfileWorkPreferences.Empty(command.ProfileId, command.WorkId),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        var current = await connection.QuerySingleOrDefaultAsync<PreferenceRow>(new CommandDefinition(
            """
            SELECT profile_id AS ProfileId,
                   work_id AS WorkId,
                   personal_notes AS PersonalNotes,
                   local_tags_json AS LocalTagsJson,
                   is_hidden AS IsHidden,
                   include_in_recommendations AS IncludeInRecommendations,
                   revision AS Revision,
                   updated_at AS UpdatedAt
            FROM profile_work_preferences
            WHERE profile_id = @profileId AND work_id = @workId;
            """,
            new { profileId = command.ProfileId, workId = command.WorkId }, transaction, cancellationToken: ct));

        var currentRevision = current?.Revision ?? 0;
        var displayOverrides = await LoadDisplayOverridesAsync(connection, transaction, command.WorkId, ct);
        if (currentRevision != command.ExpectedRevision)
        {
            transaction.Rollback();
            return new EditorPreferencesSaveResult(
                true,
                true,
                true,
                current is null ? ProfileWorkPreferences.Empty(command.ProfileId, command.WorkId) : Map(current),
                displayOverrides);
        }

        foreach (var (key, value) in command.DisplayOverrideChanges)
        {
            var normalizedValue = value?.Trim() ?? string.Empty;
            if (normalizedValue.Length == 0)
                displayOverrides.Remove(key);
            else
                displayOverrides[key] = normalizedValue;
        }

        var now = DateTimeOffset.UtcNow;
        var nextRevision = currentRevision + 1;
        var tags = command.LocalTags
            .Select(tag => tag.Trim())
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE works SET display_overrides_json = @json WHERE id = @workId;",
            new
            {
                json = displayOverrides.Count == 0 ? null : JsonSerializer.Serialize(displayOverrides),
                workId = command.WorkId,
            }, transaction, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO profile_work_preferences
                (profile_id, work_id, personal_notes, local_tags_json, is_hidden,
                 include_in_recommendations, revision, updated_at)
            VALUES
                (@profileId, @workId, @personalNotes, @localTagsJson, @isHidden,
                 @includeInRecommendations, @revision, @updatedAt)
            ON CONFLICT(profile_id, work_id) DO UPDATE SET
                personal_notes = excluded.personal_notes,
                local_tags_json = excluded.local_tags_json,
                is_hidden = excluded.is_hidden,
                include_in_recommendations = excluded.include_in_recommendations,
                revision = excluded.revision,
                updated_at = excluded.updated_at;
            """,
            new
            {
                profileId = command.ProfileId,
                workId = command.WorkId,
                personalNotes = string.IsNullOrWhiteSpace(command.PersonalNotes) ? null : command.PersonalNotes.Trim(),
                localTagsJson = tags.Count == 0 ? null : JsonSerializer.Serialize(tags),
                isHidden = command.IsHidden ? 1 : 0,
                includeInRecommendations = command.IncludeInRecommendations ? 1 : 0,
                revision = nextRevision,
                updatedAt = now.ToString("O"),
            }, transaction, cancellationToken: ct));

        transaction.Commit();
        return new EditorPreferencesSaveResult(
            true,
            true,
            false,
            new ProfileWorkPreferences(
                command.ProfileId,
                command.WorkId,
                string.IsNullOrWhiteSpace(command.PersonalNotes) ? null : command.PersonalNotes.Trim(),
                tags,
                command.IsHidden,
                command.IncludeInRecommendations,
                nextRevision,
                now),
            displayOverrides);
    }

    private static async Task<Dictionary<string, string>> LoadDisplayOverridesAsync(
        System.Data.IDbConnection connection,
        System.Data.IDbTransaction transaction,
        Guid workId,
        CancellationToken ct)
    {
        var json = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT display_overrides_json FROM works WHERE id = @workId;",
            new { workId }, transaction, cancellationToken: ct));
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return values is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static ProfileWorkPreferences Map(PreferenceRow row) => new(
        row.ProfileId,
        row.WorkId,
        row.PersonalNotes,
        ParseTags(row.LocalTagsJson),
        row.IsHidden,
        row.IncludeInRecommendations,
        row.Revision,
        DateTimeOffset.TryParse(row.UpdatedAt, out var updatedAt) ? updatedAt : null);

    private static IReadOnlyList<string> ParseTags(string? json)
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

    private sealed class PreferenceRow
    {
        public Guid ProfileId { get; set; }
        public Guid WorkId { get; set; }
        public string? PersonalNotes { get; set; }
        public string? LocalTagsJson { get; set; }
        public bool IsHidden { get; set; }
        public bool IncludeInRecommendations { get; set; }
        public long Revision { get; set; }
        public string? UpdatedAt { get; set; }
    }
}
