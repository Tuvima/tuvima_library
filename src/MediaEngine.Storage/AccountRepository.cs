using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

public sealed class AccountRepository(IDatabaseConnection db) : IAccountRepository
{
    public Task<Account?> GetByIdAsync(Guid id, CancellationToken ct = default) => GetOneAsync("id = @value", GuidSql.ToBlob(id), ct);

    public Task<Account?> GetByNormalizedEmailAsync(string normalizedEmail, CancellationToken ct = default) =>
        GetOneAsync("normalized_email = @value", normalizedEmail, ct);

    public Task<IReadOnlyList<Account>> GetAllAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = db.CreateConnection();
        var rows = conn.Query<AccountRow>(Select + " ORDER BY created_at;").Select(Map).ToList();
        return Task.FromResult<IReadOnlyList<Account>>(rows);
    }

    public Task InsertAsync(Account account, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = db.CreateConnection();
        conn.Execute("""
            INSERT INTO accounts (id, email, normalized_email, is_local_only, is_enabled, created_at, updated_at)
            VALUES (@Id, @Email, @NormalizedEmail, @IsLocalOnly, @IsEnabled, @CreatedAt, @UpdatedAt);
            """, Parameters(account));
        return Task.CompletedTask;
    }

    public Task<bool> UpdateAsync(Account account, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = db.CreateConnection();
        return Task.FromResult(conn.Execute("""
            UPDATE accounts SET email = @Email, normalized_email = @NormalizedEmail,
                is_local_only = @IsLocalOnly, is_enabled = @IsEnabled, updated_at = @UpdatedAt
            WHERE id = @Id;
            """, Parameters(account)) > 0);
    }

    public Task GrantProfileAsync(AccountProfileGrant grant, CancellationToken ct = default)
    {
        return db.ExecuteWriteAsync((conn, transaction, token) =>
        {
            token.ThrowIfCancellationRequested();
            if (grant.IsDefault)
            {
                conn.Execute("UPDATE account_profile_grants SET is_default = 0 WHERE account_id = @accountId;",
                    new { accountId = grant.AccountId }, transaction);
            }
            conn.Execute("""
                INSERT INTO account_profile_grants (account_id, profile_id, is_default, granted_at)
                VALUES (@AccountId, @ProfileId, @IsDefault, @GrantedAt)
                ON CONFLICT(account_id, profile_id) DO UPDATE SET is_default = excluded.is_default;
                """, new
            {
                grant.AccountId,
                grant.ProfileId,
                IsDefault = grant.IsDefault ? 1 : 0,
                GrantedAt = grant.GrantedAt.ToString("O"),
            }, transaction);
        }, ct);
    }

    public Task<bool> RevokeProfileAsync(Guid accountId, Guid profileId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = db.CreateConnection();
        return Task.FromResult(conn.Execute(
            "DELETE FROM account_profile_grants WHERE account_id = @accountId AND profile_id = @profileId;",
            new { accountId, profileId }) > 0);
    }

    public Task<bool> HasProfileAccessAsync(Guid accountId, Guid profileId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = db.CreateConnection();
        return Task.FromResult(conn.ExecuteScalar<int>(
            "SELECT COUNT(1) FROM account_profile_grants WHERE account_id = @accountId AND profile_id = @profileId;",
            new { accountId, profileId }) > 0);
    }

    public Task<IReadOnlyList<Guid>> GetProfileIdsAsync(Guid accountId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = db.CreateConnection();
        var ids = conn.Query<Guid>("""
            SELECT profile_id FROM account_profile_grants
            WHERE account_id = @accountId ORDER BY is_default DESC, granted_at;
            """, new { accountId }).ToList();
        return Task.FromResult<IReadOnlyList<Guid>>(ids);
    }

    public Task<Guid?> GetDefaultProfileIdAsync(Guid accountId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = db.CreateConnection();
        var value = conn.QueryFirstOrDefault<Guid?>("""
            SELECT profile_id FROM account_profile_grants WHERE account_id = @accountId
            ORDER BY is_default DESC, granted_at LIMIT 1;
            """, new { accountId });
        return Task.FromResult(value);
    }

    public Task<Guid?> GetLocalOnlyAccountIdForProfileAsync(Guid profileId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = db.CreateConnection();
        var value = conn.QueryFirstOrDefault<Guid?>("""
            SELECT a.id FROM accounts a
            JOIN account_profile_grants g ON g.account_id = a.id
            WHERE g.profile_id = @profileId AND a.is_local_only = 1 AND a.is_enabled = 1
            ORDER BY g.is_default DESC, a.created_at LIMIT 1;
            """, new { profileId });
        return Task.FromResult(value);
    }

    public Task InsertInvitationAsync(AccountInvitation invitation,CancellationToken ct=default)
    {ct.ThrowIfCancellationRequested();using var conn=db.CreateConnection();conn.Execute("INSERT INTO account_invitations(id,account_id,token_hash,created_at,expires_at,consumed_at) VALUES(@Id,@AccountId,@TokenHash,@CreatedAt,@ExpiresAt,@ConsumedAt);",new{invitation.Id,invitation.AccountId,invitation.TokenHash,CreatedAt=invitation.CreatedAt.ToString("O"),ExpiresAt=invitation.ExpiresAt.ToString("O"),ConsumedAt=invitation.ConsumedAt?.ToString("O")});return Task.CompletedTask;}

    public Task<AccountInvitation?> GetActiveInvitationAsync(string tokenHash,DateTimeOffset now,CancellationToken ct=default)
    {ct.ThrowIfCancellationRequested();using var conn=db.CreateConnection();var row=conn.QueryFirstOrDefault<InvitationRow>("SELECT id AS Id,account_id AS AccountId,token_hash AS TokenHash,created_at AS CreatedAt,expires_at AS ExpiresAt,consumed_at AS ConsumedAt FROM account_invitations WHERE token_hash=@tokenHash AND consumed_at IS NULL AND expires_at>@now LIMIT 1;",new{tokenHash,now=now.ToString("O")});return Task.FromResult(row is null?null:new AccountInvitation{Id=row.Id,AccountId=row.AccountId,TokenHash=row.TokenHash,CreatedAt=DateTimeOffset.Parse(row.CreatedAt),ExpiresAt=DateTimeOffset.Parse(row.ExpiresAt),ConsumedAt=string.IsNullOrWhiteSpace(row.ConsumedAt)?null:DateTimeOffset.Parse(row.ConsumedAt)});}

    public Task<bool> ConsumeInvitationAsync(Guid invitationId,DateTimeOffset consumedAt,CancellationToken ct=default)
    {ct.ThrowIfCancellationRequested();using var conn=db.CreateConnection();return Task.FromResult(conn.Execute("UPDATE account_invitations SET consumed_at=@consumedAt WHERE id=@invitationId AND consumed_at IS NULL;",new{invitationId,consumedAt=consumedAt.ToString("O")})>0);}

    private Task<Account?> GetOneAsync(string predicate, object value, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = db.CreateConnection();
        var row = conn.QueryFirstOrDefault<AccountRow>(Select + $" WHERE {predicate} LIMIT 1;", new { value });
        return Task.FromResult(row is null ? null : Map(row));
    }

    private const string Select = """
        SELECT id AS Id, email AS Email, normalized_email AS NormalizedEmail,
               is_local_only AS IsLocalOnly, is_enabled AS IsEnabled,
               created_at AS CreatedAt, updated_at AS UpdatedAt
        FROM accounts
        """;

    private static object Parameters(Account account) => new
    {
        account.Id,
        account.Email,
        account.NormalizedEmail,
        IsLocalOnly = account.IsLocalOnly ? 1 : 0,
        IsEnabled = account.IsEnabled ? 1 : 0,
        CreatedAt = account.CreatedAt.ToString("O"),
        UpdatedAt = account.UpdatedAt.ToString("O"),
    };

    private static Account Map(AccountRow row) => new()
    {
        Id = row.Id,
        Email = row.Email,
        NormalizedEmail = row.NormalizedEmail,
        IsLocalOnly = row.IsLocalOnly,
        IsEnabled = row.IsEnabled,
        CreatedAt = DateTimeOffset.Parse(row.CreatedAt),
        UpdatedAt = DateTimeOffset.Parse(row.UpdatedAt),
    };

    private sealed class AccountRow
    {
        public Guid Id { get; set; }
        public string? Email { get; set; }
        public string? NormalizedEmail { get; set; }
        public bool IsLocalOnly { get; set; }
        public bool IsEnabled { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
        public string UpdatedAt { get; set; } = string.Empty;
    }
    private sealed class InvitationRow{public Guid Id{get;set;}public Guid AccountId{get;set;}public string TokenHash{get;set;}="";public string CreatedAt{get;set;}="";public string ExpiresAt{get;set;}="";public string? ConsumedAt{get;set;}}
}
