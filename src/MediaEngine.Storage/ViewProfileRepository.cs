using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.PersonalMedia;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

public sealed class ViewProfileRepository(IDatabaseConnection database) : IViewProfileRepository
{
    public Task<ViewProfilePolicy> GetPolicyAsync(Guid profileId, CancellationToken ct = default)
    {
        ValidateId(profileId, nameof(profileId));
        ct.ThrowIfCancellationRequested();
        using var connection = database.CreateConnection();
        var row = connection.QuerySingleOrDefault<PolicyRow>(new CommandDefinition("""
            SELECT profile_id AS ProfileId, view_enabled AS ViewEnabled,
                   access_shared_view AS AccessSharedView,
                   include_in_shared_view AS IncludeInSharedView,
                   share_galleries AS ShareGalleries, updated_at AS UpdatedAt
              FROM profile_view_policies
             WHERE profile_id = @profileId;
            """, new { profileId }, cancellationToken: ct));
        return Task.FromResult(row is null ? ViewProfilePolicy.Default(profileId) : Map(row));
    }

    public Task<bool> SavePolicyAsync(ViewProfilePolicy policy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ValidateId(policy.ProfileId, nameof(policy));
        return database.ExecuteWriteAsync((connection, transaction, token) =>
        {
            token.ThrowIfCancellationRequested();
            if (!ProfileExists(connection, transaction, policy.ProfileId, token)) return false;
            var now = DateTimeOffset.UtcNow;
            connection.Execute(new CommandDefinition("""
                INSERT INTO profile_view_policies
                    (profile_id, view_enabled, access_shared_view, include_in_shared_view,
                     share_galleries, updated_at)
                VALUES
                    (@ProfileId, @ViewEnabled, @AccessSharedView, @IncludeInSharedView,
                     @ShareGalleries, @now)
                ON CONFLICT(profile_id) DO UPDATE SET
                    view_enabled = excluded.view_enabled,
                    access_shared_view = excluded.access_shared_view,
                    include_in_shared_view = excluded.include_in_shared_view,
                    share_galleries = excluded.share_galleries,
                    updated_at = excluded.updated_at;
                """, new
            {
                policy.ProfileId,
                ViewEnabled = policy.ViewEnabled ? 1 : 0,
                AccessSharedView = policy.AccessSharedView ? 1 : 0,
                IncludeInSharedView = policy.IncludeInSharedView ? 1 : 0,
                ShareGalleries = policy.ShareGalleries ? 1 : 0,
                now,
            }, transaction, cancellationToken: token));
            return true;
        }, ct);
    }

    public Task<ViewProfilePreferences> GetPreferencesAsync(Guid profileId, CancellationToken ct = default)
    {
        ValidateId(profileId, nameof(profileId));
        ct.ThrowIfCancellationRequested();
        using var connection = database.CreateConnection();
        var row = connection.QuerySingleOrDefault<PreferencesRow>(new CommandDefinition("""
            SELECT profile_id AS ProfileId, last_scope_kind AS LastScopeKind,
                   last_scope_profile_id AS LastScopeProfileId,
                   timeline_density AS TimelineDensity, updated_at AS UpdatedAt
              FROM profile_view_preferences
             WHERE profile_id = @profileId;
            """, new { profileId }, cancellationToken: ct));
        return Task.FromResult(row is null ? ViewProfilePreferences.Default(profileId) : Map(row));
    }

    public Task<bool> SavePreferencesAsync(ViewProfilePreferences preferences, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ValidateId(preferences.ProfileId, nameof(preferences));
        if ((preferences.LastScopeKind == ViewScopeKind.Profile) != preferences.LastScopeProfileId.HasValue)
        {
            throw new ArgumentException("A profile scope requires exactly one target profile.", nameof(preferences));
        }
        if (preferences.LastScopeProfileId == Guid.Empty)
        {
            throw new ArgumentException("Scope profile ID cannot be empty.", nameof(preferences));
        }

        return database.ExecuteWriteAsync((connection, transaction, token) =>
        {
            token.ThrowIfCancellationRequested();
            if (!ProfileExists(connection, transaction, preferences.ProfileId, token)) return false;
            if (preferences.LastScopeProfileId.HasValue
                && !ProfileExists(connection, transaction, preferences.LastScopeProfileId.Value, token))
            {
                return false;
            }
            var now = DateTimeOffset.UtcNow;
            connection.Execute(new CommandDefinition("""
                INSERT INTO profile_view_preferences
                    (profile_id, last_scope_kind, last_scope_profile_id, timeline_density, updated_at)
                VALUES (@ProfileId, @ScopeKind, @LastScopeProfileId, @Density, @now)
                ON CONFLICT(profile_id) DO UPDATE SET
                    last_scope_kind = excluded.last_scope_kind,
                    last_scope_profile_id = excluded.last_scope_profile_id,
                    timeline_density = excluded.timeline_density,
                    updated_at = excluded.updated_at;
                """, new
            {
                preferences.ProfileId,
                ScopeKind = preferences.LastScopeKind is null ? null : ToStorage(preferences.LastScopeKind.Value),
                preferences.LastScopeProfileId,
                Density = ToStorage(preferences.TimelineDensity),
                now,
            }, transaction, cancellationToken: token));
            return true;
        }, ct);
    }

    private static bool ProfileExists(
        System.Data.IDbConnection connection,
        System.Data.IDbTransaction transaction,
        Guid profileId,
        CancellationToken ct) =>
        connection.ExecuteScalar<long>(new CommandDefinition(
            "SELECT COUNT(1) FROM profiles WHERE id = @profileId;",
            new { profileId }, transaction, cancellationToken: ct)) != 0;

    private static ViewProfilePolicy Map(PolicyRow row) => new(
        row.ProfileId, row.ViewEnabled != 0, row.AccessSharedView != 0,
        row.IncludeInSharedView != 0, row.ShareGalleries != 0, ParseDate(row.UpdatedAt));

    private static ViewProfilePreferences Map(PreferencesRow row) => new(
        row.ProfileId,
        row.LastScopeKind is null ? null : ParseScope(row.LastScopeKind),
        row.LastScopeProfileId,
        ParseDensity(row.TimelineDensity),
        ParseDate(row.UpdatedAt));

    private static string ToStorage(ViewScopeKind value) => value switch
    {
        ViewScopeKind.Shared => "shared",
        ViewScopeKind.Mine => "mine",
        ViewScopeKind.Profile => "profile",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static ViewScopeKind ParseScope(string value) => value switch
    {
        "shared" => ViewScopeKind.Shared,
        "mine" => ViewScopeKind.Mine,
        "profile" => ViewScopeKind.Profile,
        _ => throw new InvalidOperationException($"Unsupported stored View scope '{value}'."),
    };

    private static string ToStorage(ViewTimelineDensity value) => value switch
    {
        ViewTimelineDensity.Compact => "compact",
        ViewTimelineDensity.Comfortable => "comfortable",
        ViewTimelineDensity.Relaxed => "relaxed",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static ViewTimelineDensity ParseDensity(string value) => value switch
    {
        "compact" => ViewTimelineDensity.Compact,
        "comfortable" => ViewTimelineDensity.Comfortable,
        "relaxed" => ViewTimelineDensity.Relaxed,
        _ => throw new InvalidOperationException($"Unsupported stored View density '{value}'."),
    };

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private static void ValidateId(Guid value, string parameterName)
    {
        if (value == Guid.Empty) throw new ArgumentException("Profile ID is required.", parameterName);
    }

    private sealed class PolicyRow
    {
        public Guid ProfileId { get; init; }
        public long ViewEnabled { get; init; }
        public long AccessSharedView { get; init; }
        public long IncludeInSharedView { get; init; }
        public long ShareGalleries { get; init; }
        public string? UpdatedAt { get; init; }
    }

    private sealed class PreferencesRow
    {
        public Guid ProfileId { get; init; }
        public string? LastScopeKind { get; init; }
        public Guid? LastScopeProfileId { get; init; }
        public string TimelineDensity { get; init; } = "comfortable";
        public string? UpdatedAt { get; init; }
    }
}
