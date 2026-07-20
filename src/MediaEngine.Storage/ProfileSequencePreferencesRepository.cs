using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

public sealed class ProfileSequencePreferencesRepository : IProfileSequencePreferencesRepository
{
    private readonly IDatabaseConnection _db;

    public ProfileSequencePreferencesRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    public async Task<ProfileSequencePreference?> GetAsync(
        Guid profileId,
        string mediaType,
        string containerKey,
        CancellationToken ct = default)
    {
        var normalizedMediaType = NormalizeRequired(mediaType, nameof(mediaType));
        var normalizedContainerKey = NormalizeRequired(containerKey, nameof(containerKey));

        using var connection = _db.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<PreferenceRow>(new CommandDefinition(
            """
            SELECT profile_id AS ProfileId,
                   media_type AS MediaType,
                   container_key AS ContainerKey,
                   show_missing AS ShowMissing,
                   updated_at AS UpdatedAt
            FROM profile_sequence_preferences
            WHERE profile_id = @profileId
              AND media_type = @mediaType
              AND container_key = @containerKey;
            """,
            new
            {
                profileId,
                mediaType = normalizedMediaType,
                containerKey = normalizedContainerKey,
            },
            cancellationToken: ct));

        return row is null ? null : Map(row);
    }

    public async Task<ProfileSequencePreferenceSaveResult> SaveAsync(
        Guid profileId,
        string mediaType,
        string containerKey,
        bool showMissing,
        CancellationToken ct = default)
    {
        var normalizedMediaType = NormalizeRequired(mediaType, nameof(mediaType));
        var normalizedContainerKey = NormalizeRequired(containerKey, nameof(containerKey));

        using var connection = _db.CreateConnection();
        var profileExists = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(1) FROM profiles WHERE id = @profileId;",
            new { profileId },
            cancellationToken: ct)) > 0;
        if (!profileExists)
        {
            return new ProfileSequencePreferenceSaveResult(false, null);
        }

        var now = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO profile_sequence_preferences
                (profile_id, media_type, container_key, show_missing, updated_at)
            VALUES
                (@profileId, @mediaType, @containerKey, @showMissing, @updatedAt)
            ON CONFLICT(profile_id, media_type, container_key) DO UPDATE SET
                show_missing = excluded.show_missing,
                updated_at = excluded.updated_at;
            """,
            new
            {
                profileId,
                mediaType = normalizedMediaType,
                containerKey = normalizedContainerKey,
                showMissing = showMissing ? 1 : 0,
                updatedAt = now.ToString("O"),
            },
            cancellationToken: ct));

        return new ProfileSequencePreferenceSaveResult(
            true,
            new ProfileSequencePreference(
                profileId,
                normalizedMediaType,
                normalizedContainerKey,
                showMissing,
                now));
    }

    public async Task<bool> DeleteAsync(
        Guid profileId,
        string mediaType,
        string containerKey,
        CancellationToken ct = default)
    {
        var normalizedMediaType = NormalizeRequired(mediaType, nameof(mediaType));
        var normalizedContainerKey = NormalizeRequired(containerKey, nameof(containerKey));

        using var connection = _db.CreateConnection();
        var deleted = await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM profile_sequence_preferences
            WHERE profile_id = @profileId
              AND media_type = @mediaType
              AND container_key = @containerKey;
            """,
            new
            {
                profileId,
                mediaType = normalizedMediaType,
                containerKey = normalizedContainerKey,
            },
            cancellationToken: ct));
        return deleted > 0;
    }

    private static string NormalizeRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }

        return value.Trim().ToLowerInvariant();
    }

    private static ProfileSequencePreference Map(PreferenceRow row) => new(
        row.ProfileId,
        row.MediaType,
        row.ContainerKey,
        row.ShowMissing,
        DateTimeOffset.Parse(row.UpdatedAt, System.Globalization.CultureInfo.InvariantCulture));

    private sealed class PreferenceRow
    {
        public Guid ProfileId { get; set; }
        public string MediaType { get; set; } = string.Empty;
        public string ContainerKey { get; set; } = string.Empty;
        public bool ShowMissing { get; set; }
        public string UpdatedAt { get; set; } = string.Empty;
    }
}
