using System.Text.Json;
using Dapper;
using MediaEngine.Contracts.Items;
using MediaEngine.Contracts.Persons;
using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Contracts;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.ReadServices;

public sealed class PersonEditorReadService
{
    private readonly IDatabaseConnection _db;
    private readonly IPersonRepository _persons;

    public PersonEditorReadService(IDatabaseConnection db, IPersonRepository persons)
    {
        _db = db;
        _persons = persons;
    }

    public async Task<PersonEditorStateResponse?> GetAsync(Guid personId, Guid? profileId, CancellationToken ct)
    {
        var person = await _persons.FindByIdAsync(personId, ct);
        if (person is null)
            return null;

        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var state = conn.QueryFirstOrDefault<PersonEditorStateRow>("""
            SELECT p.display_overrides_json AS DisplayOverridesJson,
                   pp.local_tags_json AS LocalTagsJson,
                   COALESCE(pp.revision, 0) AS Revision,
                   pp.updated_at AS UpdatedAt
            FROM persons p
            LEFT JOIN profile_person_preferences pp
              ON pp.person_id = p.id AND pp.profile_id = @profileId
            WHERE p.id = @personId
            LIMIT 1;
            """, new { personId, profileId });

        var history = conn.Query<PersonHistoryRow>("""
            SELECT id AS Id, occurred_at AS OccurredAt, action_type AS ActionType,
                   detail AS Detail, profile_id AS ProfileId
            FROM system_activity
            WHERE entity_id = @personId AND entity_type = 'Person'
            ORDER BY occurred_at DESC
            LIMIT 200;
            """, new { personId })
            .Select(row => new LibraryItemHistoryDto
            {
                Id = row.Id.ToString(),
                EntityId = personId,
                OccurredAt = DateTimeOffset.TryParse(row.OccurredAt, out var occurredAt) ? occurredAt : DateTimeOffset.UtcNow,
                EventType = row.ActionType,
                Label = FormatHistoryLabel(row.ActionType),
                Detail = row.Detail,
                Category = ClassifyHistory(row.ActionType),
                ActorLabel = row.ProfileId.HasValue ? "Library user" : "System",
            })
            .ToList();

        return new PersonEditorStateResponse
        {
            PersonId = personId,
            BaselineName = person.Name,
            BaselineBiography = person.Biography,
            DisplayOverrides = DeserializeStringMap(state?.DisplayOverridesJson),
            LocalTags = DeserializeStringList(state?.LocalTagsJson),
            Revision = state?.Revision ?? 0,
            UpdatedAt = DateTimeOffset.TryParse(state?.UpdatedAt, out var updatedAt) ? updatedAt : null,
            History = history,
        };
    }

    public IReadOnlyDictionary<string, string> GetDisplayOverrides(Guid personId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var json = conn.QueryFirstOrDefault<string?>(
            "SELECT display_overrides_json FROM persons WHERE id = @personId LIMIT 1;",
            new { personId });
        return DeserializeStringMap(json);
    }

    public Task<PersonEditorWriteResult> SaveAsync(Guid personId, PersonEditorSaveRequest request, CancellationToken ct) =>
        _db.ExecuteWriteAsync((conn, tx, innerCt) =>
        {
            innerCt.ThrowIfCancellationRequested();
            var revision = request.ProfileId.HasValue
                ? conn.QueryFirstOrDefault<long?>("""
                    SELECT revision FROM profile_person_preferences
                    WHERE profile_id = @profileId AND person_id = @personId;
                    """, new { profileId = request.ProfileId, personId }, tx) ?? 0
                : 0;
            if (request.ProfileId.HasValue && revision != request.ExpectedRevision)
                return new PersonEditorWriteResult(false, revision);

            var normalizedOverrides = request.DisplayOverrides
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .ToDictionary(pair => pair.Key, pair => pair.Value.Trim(), StringComparer.OrdinalIgnoreCase);
            conn.Execute("UPDATE persons SET display_overrides_json = @json WHERE id = @personId;",
                new { personId, json = normalizedOverrides.Count == 0 ? null : JsonSerializer.Serialize(normalizedOverrides) }, tx);

            var nextRevision = revision;
            if (request.ProfileId is { } profileId)
            {
                nextRevision++;
                var tags = request.LocalTags
                    .Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Select(tag => tag.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                conn.Execute("""
                    INSERT INTO profile_person_preferences
                        (profile_id, person_id, local_tags_json, revision, updated_at)
                    VALUES (@profileId, @personId, @tags, @revision, @updatedAt)
                    ON CONFLICT(profile_id, person_id) DO UPDATE SET
                        local_tags_json = excluded.local_tags_json,
                        revision = excluded.revision,
                        updated_at = excluded.updated_at;
                    """, new
                {
                    profileId,
                    personId,
                    tags = tags.Count == 0 ? null : JsonSerializer.Serialize(tags),
                    revision = nextRevision,
                    updatedAt = DateTimeOffset.UtcNow.ToString("O"),
                }, tx);
            }

            conn.Execute("""
                INSERT INTO system_activity (action_type, entity_id, entity_type, profile_id, changes_json, detail)
                VALUES (@actionType, @personId, 'Person', @profileId, @changes, @detail);
                """, new
            {
                actionType = SystemActionType.MetadataManualOverride,
                personId,
                profileId = request.ProfileId,
                changes = JsonSerializer.Serialize(new { display_overrides = normalizedOverrides.Keys, local_tags = request.LocalTags.Count }),
                detail = "Person details and local library fields updated",
            }, tx);

            return new PersonEditorWriteResult(true, nextRevision);
        }, ct);

    private static IReadOnlyDictionary<string, string> DeserializeStringMap(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static IReadOnlyList<string> DeserializeStringList(string? json)
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

    private static string FormatHistoryLabel(string actionType) => actionType switch
    {
        SystemActionType.PersonHydrated => "Identity enriched",
        SystemActionType.CoverArtSaved => "Artwork updated",
        SystemActionType.MetadataManualOverride => "Person edited",
        _ => actionType,
    };

    private static string ClassifyHistory(string actionType) => actionType switch
    {
        SystemActionType.CoverArtSaved => "artwork",
        SystemActionType.PersonHydrated => "match",
        _ => "metadata",
    };

    private sealed record PersonEditorStateRow(string? DisplayOverridesJson, string? LocalTagsJson, long Revision, string? UpdatedAt);
    private sealed record PersonHistoryRow(long Id, string OccurredAt, string ActionType, string? Detail, Guid? ProfileId);
}

public sealed record PersonEditorWriteResult(bool Saved, long Revision);
