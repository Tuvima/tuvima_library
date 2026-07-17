namespace MediaEngine.Domain.Contracts;

public interface IProfileWorkPreferencesRepository
{
    Task<ProfileWorkPreferences> GetAsync(Guid profileId, Guid workId, CancellationToken ct = default);

    Task<EditorPreferencesSaveResult> SaveAsync(
        EditorPreferencesSaveCommand command,
        CancellationToken ct = default);
}

public sealed record ProfileWorkPreferences(
    Guid ProfileId,
    Guid WorkId,
    string? PersonalNotes,
    IReadOnlyList<string> LocalTags,
    bool IsHidden,
    bool IncludeInRecommendations,
    long Revision,
    DateTimeOffset? UpdatedAt)
{
    public static ProfileWorkPreferences Empty(Guid profileId, Guid workId) =>
        new(profileId, workId, null, [], false, true, 0, null);
}

public sealed record EditorPreferencesSaveCommand(
    Guid ProfileId,
    Guid WorkId,
    long ExpectedRevision,
    IReadOnlyDictionary<string, string> DisplayOverrideChanges,
    string? PersonalNotes,
    IReadOnlyList<string> LocalTags,
    bool IsHidden,
    bool IncludeInRecommendations);

public sealed record EditorPreferencesSaveResult(
    bool WorkExists,
    bool ProfileExists,
    bool Conflict,
    ProfileWorkPreferences Preferences,
    IReadOnlyDictionary<string, string> DisplayOverrides);
