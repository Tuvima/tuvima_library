using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

public sealed class ProfileExternalLoginRepository : IProfileExternalLoginRepository
{
    private readonly IDatabaseConnection _db;

    public ProfileExternalLoginRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    public Task<IReadOnlyList<ProfileExternalLogin>> GetByProfileAsync(Guid profileId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var rows = conn.Query<ProfileExternalLoginRow>("""
            SELECT id AS Id,
                   profile_id AS ProfileId,
                   provider AS Provider,
                   subject AS Subject,
                   email AS Email,
                   display_name AS DisplayName,
                   linked_at AS LinkedAt,
                   last_login_at AS LastLoginAt
            FROM profile_external_logins
            WHERE profile_id = @profileId
            ORDER BY linked_at ASC;
            """, new { profileId = profileId.ToString() }).AsList();

        return Task.FromResult<IReadOnlyList<ProfileExternalLogin>>(rows.Select(MapRow).ToList());
    }

    public Task<ProfileExternalLogin?> GetByProviderSubjectAsync(
        string provider,
        string subject,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var row = conn.QueryFirstOrDefault<ProfileExternalLoginRow>("""
            SELECT id AS Id,
                   profile_id AS ProfileId,
                   provider AS Provider,
                   subject AS Subject,
                   email AS Email,
                   display_name AS DisplayName,
                   linked_at AS LinkedAt,
                   last_login_at AS LastLoginAt
            FROM profile_external_logins
            WHERE provider = @provider
              AND subject = @subject
            LIMIT 1;
            """, new { provider, subject });

        return Task.FromResult(row is null ? null : MapRow(row));
    }

    public Task InsertAsync(ProfileExternalLogin login, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(login);

        using var conn = _db.CreateConnection();
        conn.Execute("""
            INSERT INTO profile_external_logins
                (id, profile_id, provider, subject, email, display_name, linked_at, last_login_at)
            VALUES
                (@id, @profileId, @provider, @subject, @email, @displayName, @linkedAt, @lastLoginAt);
            """, new
        {
            id = login.Id.ToString(),
            profileId = login.ProfileId.ToString(),
            provider = login.Provider,
            subject = login.Subject,
            email = login.Email,
            displayName = login.DisplayName,
            linkedAt = login.LinkedAt.ToString("O"),
            lastLoginAt = login.LastLoginAt?.ToString("O"),
        });

        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var rows = conn.Execute("DELETE FROM profile_external_logins WHERE id = @id;", new { id = id.ToString() });
        return Task.FromResult(rows > 0);
    }

    public Task<bool> TouchLastLoginAsync(Guid id, DateTimeOffset lastLoginAt, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var rows = conn.Execute("""
            UPDATE profile_external_logins
            SET last_login_at = @lastLoginAt
            WHERE id = @id;
            """, new { id = id.ToString(), lastLoginAt = lastLoginAt.ToString("O") });

        return Task.FromResult(rows > 0);
    }

    private sealed class ProfileExternalLoginRow
    {
        public string Id { get; set; } = string.Empty;
        public string ProfileId { get; set; } = string.Empty;
        public string Provider { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? DisplayName { get; set; }
        public string LinkedAt { get; set; } = string.Empty;
        public string? LastLoginAt { get; set; }
    }

    private static ProfileExternalLogin MapRow(ProfileExternalLoginRow row) => new()
    {
        Id = Guid.Parse(row.Id),
        ProfileId = Guid.Parse(row.ProfileId),
        Provider = row.Provider,
        Subject = row.Subject,
        Email = row.Email,
        DisplayName = row.DisplayName,
        LinkedAt = DateTimeOffset.Parse(row.LinkedAt),
        LastLoginAt = string.IsNullOrWhiteSpace(row.LastLoginAt)
            ? null
            : DateTimeOffset.Parse(row.LastLoginAt),
    };
}
