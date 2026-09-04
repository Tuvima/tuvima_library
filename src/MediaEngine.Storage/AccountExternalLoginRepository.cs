using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

public sealed class AccountExternalLoginRepository : IAccountExternalLoginRepository
{
    private readonly IDatabaseConnection _db;

    public AccountExternalLoginRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    public Task<IReadOnlyList<AccountExternalLogin>> GetByAccountAsync(Guid accountId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var rows = conn.Query<AccountExternalLoginRow>("""
            SELECT id AS Id,
                   account_id AS AccountId,
                   provider AS Provider,
                   issuer AS Issuer,
                   subject AS Subject,
                   email AS Email,
                   display_name AS DisplayName,
                   linked_at AS LinkedAt,
                   last_login_at AS LastLoginAt
            FROM account_external_logins
            WHERE account_id = @accountId
            ORDER BY linked_at ASC;
            """, new { accountId }).AsList();

        return Task.FromResult<IReadOnlyList<AccountExternalLogin>>(rows.Select(MapRow).ToList());
    }

    public Task<AccountExternalLogin?> GetByProviderSubjectAsync(
        string provider,
        string issuer,
        string subject,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var row = conn.QueryFirstOrDefault<AccountExternalLoginRow>("""
            SELECT id AS Id,
                   account_id AS AccountId,
                   provider AS Provider,
                   issuer AS Issuer,
                   subject AS Subject,
                   email AS Email,
                   display_name AS DisplayName,
                   linked_at AS LinkedAt,
                   last_login_at AS LastLoginAt
            FROM account_external_logins
            WHERE provider = @provider
              AND issuer = @issuer
              AND subject = @subject
            LIMIT 1;
            """, new { provider, issuer, subject });

        return Task.FromResult(row is null ? null : MapRow(row));
    }

    public Task InsertAsync(AccountExternalLogin login, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(login);

        using var conn = _db.CreateConnection();
        conn.Execute("""
            INSERT INTO account_external_logins
                (id, account_id, provider, issuer, subject, email, display_name, linked_at, last_login_at)
            VALUES
                (@id, @accountId, @provider, @issuer, @subject, @email, @displayName, @linkedAt, @lastLoginAt);
            """, new
        {
            id = login.Id,
            accountId = login.AccountId,
            provider = login.Provider,
            issuer = login.Issuer,
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
        var rows = conn.Execute("DELETE FROM account_external_logins WHERE id = @id;", new { id });
        return Task.FromResult(rows > 0);
    }

    public Task<bool> TouchLastLoginAsync(Guid id, DateTimeOffset lastLoginAt, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var rows = conn.Execute("""
            UPDATE account_external_logins
            SET last_login_at = @lastLoginAt
            WHERE id = @id;
            """, new { id, lastLoginAt = lastLoginAt.ToString("O") });

        return Task.FromResult(rows > 0);
    }

    private sealed class AccountExternalLoginRow
    {
        public Guid Id { get; set; }
        public Guid AccountId { get; set; }
        public string Provider { get; set; } = string.Empty;
        public string Issuer { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? DisplayName { get; set; }
        public string LinkedAt { get; set; } = string.Empty;
        public string? LastLoginAt { get; set; }
    }

    private static AccountExternalLogin MapRow(AccountExternalLoginRow row) => new()
    {
        Id = row.Id,
        AccountId = row.AccountId,
        Provider = row.Provider,
        Issuer = row.Issuer,
        Subject = row.Subject,
        Email = row.Email,
        DisplayName = row.DisplayName,
        LinkedAt = DateTimeOffset.Parse(row.LinkedAt),
        LastLoginAt = string.IsNullOrWhiteSpace(row.LastLoginAt)
            ? null
            : DateTimeOffset.Parse(row.LastLoginAt),
    };
}
