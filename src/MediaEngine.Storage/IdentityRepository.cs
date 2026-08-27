using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

public sealed class IdentityRepository(IDatabaseConnection db) : IIdentityRepository
{
    public Task<ProfileCredential?> GetCredentialByUsernameAsync(string normalizedUsername, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = db.CreateConnection();
        var row = conn.QueryFirstOrDefault<CredentialRow>(CredentialSelect +
            " WHERE normalized_username = @normalizedUsername LIMIT 1;", new { normalizedUsername });
        return Task.FromResult(row is null ? null : Map(row));
    }

    public Task<ProfileCredential?> GetCredentialAsync(Guid profileId, ProfileCredentialKind kind, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = db.CreateConnection();
        var row = conn.QueryFirstOrDefault<CredentialRow>(CredentialSelect +
            " WHERE profile_id = @profileId AND credential_kind = @kind LIMIT 1;",
            new { profileId, kind = kind.ToString() });
        return Task.FromResult(row is null ? null : Map(row));
    }

    public Task UpsertCredentialAsync(ProfileCredential credential, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = db.CreateConnection();
        conn.Execute("""
            INSERT INTO profile_credentials
                (id, profile_id, credential_kind, normalized_username, secret_hash, hash_scheme,
                 hash_version, security_stamp, failed_attempt_count, locked_until, created_at, updated_at, last_used_at)
            VALUES
                (@Id, @ProfileId, @Kind, @NormalizedUsername, @SecretHash, @HashScheme,
                 @HashVersion, @SecurityStamp, @FailedAttemptCount, @LockedUntil, @CreatedAt, @UpdatedAt, @LastUsedAt)
            ON CONFLICT(profile_id, credential_kind) DO UPDATE SET
                normalized_username = excluded.normalized_username,
                secret_hash = excluded.secret_hash,
                hash_scheme = excluded.hash_scheme,
                hash_version = excluded.hash_version,
                security_stamp = excluded.security_stamp,
                failed_attempt_count = excluded.failed_attempt_count,
                locked_until = excluded.locked_until,
                updated_at = excluded.updated_at,
                last_used_at = excluded.last_used_at;
            """, ToParameters(credential));
        return Task.CompletedTask;
    }

    public Task DeleteCredentialAsync(Guid profileId, ProfileCredentialKind kind, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = db.CreateConnection();
        conn.Execute(
            "DELETE FROM profile_credentials WHERE profile_id = @profileId AND credential_kind = @kind;",
            new { profileId, kind = kind.ToString() });
        return Task.CompletedTask;
    }

    public Task UpdateCredentialAttemptAsync(Guid credentialId, int failedAttemptCount, DateTimeOffset? lockedUntil, DateTimeOffset? lastUsedAt, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = db.CreateConnection();
        conn.Execute("""
            UPDATE profile_credentials
            SET failed_attempt_count = @failedAttemptCount,
                locked_until = @lockedUntil,
                last_used_at = COALESCE(@lastUsedAt, last_used_at),
                updated_at = @updatedAt
            WHERE id = @credentialId;
            """, new
        {
            credentialId,
            failedAttemptCount,
            lockedUntil = lockedUntil?.ToString("O"),
            lastUsedAt = lastUsedAt?.ToString("O"),
            updatedAt = DateTimeOffset.UtcNow.ToString("O"),
        });
        return Task.CompletedTask;
    }

    public Task<bool> HasAdministratorPasswordAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = db.CreateConnection();
        var count = conn.ExecuteScalar<int>("""
            SELECT COUNT(1)
            FROM profile_credentials c
            JOIN profiles p ON p.id = c.profile_id
            WHERE c.credential_kind = 'Password' AND p.role = 'Administrator';
            """);
        return Task.FromResult(count > 0);
    }

    public Task InsertSessionAsync(AuthSession session, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = db.CreateConnection();
        conn.Execute("""
            INSERT INTO auth_sessions
                (id, profile_id, active_profile_id, token_hash, device_id, device_name, client,
                 authentication_method, security_stamp, created_at, last_seen_at, expires_at,
                 revoked_at, revoked_reason)
            VALUES
                (@Id, @ProfileId, @ActiveProfileId, @TokenHash, @DeviceId, @DeviceName, @Client,
                 @AuthenticationMethod, @SecurityStamp, @CreatedAt, @LastSeenAt, @ExpiresAt,
                 @RevokedAt, @RevokedReason);
            """, ToParameters(session));
        return Task.CompletedTask;
    }

    public Task<AuthSession?> GetSessionByTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
        GetSessionAsync("token_hash = @value", tokenHash, ct);

    public Task<AuthSession?> GetSessionByIdAsync(Guid sessionId, CancellationToken ct = default) =>
        GetSessionAsync("id = @value", sessionId, ct);

    public Task<IReadOnlyList<AuthSession>> GetSessionsAsync(Guid profileId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = db.CreateConnection();
        var rows = conn.Query<SessionRow>(SessionSelect +
            " WHERE profile_id = @profileId ORDER BY last_seen_at DESC;", new { profileId }).AsList();
        return Task.FromResult<IReadOnlyList<AuthSession>>(rows.ConvertAll(Map));
    }

    public Task TouchSessionAsync(Guid sessionId, DateTimeOffset lastSeenAt, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = db.CreateConnection();
        conn.Execute("UPDATE auth_sessions SET last_seen_at = @lastSeenAt WHERE id = @sessionId;",
            new { sessionId, lastSeenAt = lastSeenAt.ToString("O") });
        return Task.CompletedTask;
    }

    public Task<bool> UpdateActiveProfileAsync(Guid sessionId, Guid activeProfileId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = db.CreateConnection();
        return Task.FromResult(conn.Execute(
            "UPDATE auth_sessions SET active_profile_id = @activeProfileId WHERE id = @sessionId AND revoked_at IS NULL;",
            new { sessionId, activeProfileId }) > 0);
    }

    public Task<bool> RevokeSessionAsync(Guid sessionId, DateTimeOffset revokedAt, string reason, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = db.CreateConnection();
        var changed = conn.Execute("""
            UPDATE auth_sessions SET revoked_at = @revokedAt, revoked_reason = @reason
            WHERE id = @sessionId AND revoked_at IS NULL;
            """, new { sessionId, revokedAt = revokedAt.ToString("O"), reason });
        return Task.FromResult(changed > 0);
    }

    public Task<int> RevokeProfileSessionsAsync(Guid profileId, DateTimeOffset revokedAt, string reason, Guid? exceptSessionId = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = db.CreateConnection();
        var changed = conn.Execute("""
            UPDATE auth_sessions SET revoked_at = @revokedAt, revoked_reason = @reason
            WHERE profile_id = @profileId AND revoked_at IS NULL
              AND (@exceptSessionId IS NULL OR id <> @exceptSessionId);
            """, new { profileId, revokedAt = revokedAt.ToString("O"), reason, exceptSessionId });
        return Task.FromResult(changed);
    }

    public Task InsertRecoveryCodesAsync(IReadOnlyList<PasswordRecoveryCode> codes, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (codes.Count == 0) return Task.CompletedTask;
        return db.ExecuteWriteAsync((conn, tx, _) =>
        {
            conn.Execute("""
                INSERT INTO password_recovery_codes (id, profile_id, code_hash, created_at, expires_at, consumed_at)
                VALUES (@Id, @ProfileId, @CodeHash, @CreatedAt, @ExpiresAt, @ConsumedAt);
                """, codes.Select(code => new
            {
                code.Id,
                code.ProfileId,
                code.CodeHash,
                CreatedAt = code.CreatedAt.ToString("O"),
                ExpiresAt = code.ExpiresAt.ToString("O"),
                ConsumedAt = code.ConsumedAt?.ToString("O"),
            }), tx);
        }, ct);
    }

    public Task<PasswordRecoveryCode?> GetActiveRecoveryCodeAsync(Guid profileId, string codeHash, DateTimeOffset now, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = db.CreateConnection();
        var row = conn.QueryFirstOrDefault<RecoveryRow>("""
            SELECT id AS Id, profile_id AS ProfileId, code_hash AS CodeHash, created_at AS CreatedAt,
                   expires_at AS ExpiresAt, consumed_at AS ConsumedAt
            FROM password_recovery_codes
            WHERE profile_id = @profileId AND code_hash = @codeHash AND consumed_at IS NULL AND expires_at > @now
            LIMIT 1;
            """, new { profileId, codeHash, now = now.ToString("O") });
        return Task.FromResult(row is null ? null : Map(row));
    }

    public Task<bool> ConsumeRecoveryCodeAsync(Guid codeId, DateTimeOffset consumedAt, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = db.CreateConnection();
        var changed = conn.Execute("""
            UPDATE password_recovery_codes SET consumed_at = @consumedAt
            WHERE id = @codeId AND consumed_at IS NULL;
            """, new { codeId, consumedAt = consumedAt.ToString("O") });
        return Task.FromResult(changed > 0);
    }

    public Task DeleteRecoveryCodesAsync(Guid profileId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = db.CreateConnection();
        conn.Execute("DELETE FROM password_recovery_codes WHERE profile_id = @profileId;", new { profileId });
        return Task.CompletedTask;
    }

    public Task<ServiceCredential?> GetActiveServiceCredentialAsync(string purpose, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = db.CreateConnection();
        var row = conn.QueryFirstOrDefault<ServiceRow>(ServiceSelect +
            " WHERE purpose = @purpose AND revoked_at IS NULL LIMIT 1;", new { purpose });
        return Task.FromResult(row is null ? null : Map(row));
    }

    public Task<ServiceCredential?> GetServiceCredentialByHashAsync(string tokenHash, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = db.CreateConnection();
        var row = conn.QueryFirstOrDefault<ServiceRow>(ServiceSelect +
            " WHERE token_hash = @tokenHash AND revoked_at IS NULL LIMIT 1;", new { tokenHash });
        return Task.FromResult(row is null ? null : Map(row));
    }

    public Task InsertServiceCredentialAsync(ServiceCredential credential, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = db.CreateConnection();
        conn.Execute("""
            INSERT INTO service_credentials (id, purpose, key_id, token_hash, created_at, last_used_at, revoked_at)
            VALUES (@Id, @Purpose, @KeyId, @TokenHash, @CreatedAt, @LastUsedAt, @RevokedAt);
            """, new
        {
            credential.Id,
            credential.Purpose,
            credential.KeyId,
            credential.TokenHash,
            CreatedAt = credential.CreatedAt.ToString("O"),
            LastUsedAt = credential.LastUsedAt?.ToString("O"),
            RevokedAt = credential.RevokedAt?.ToString("O"),
        });
        return Task.CompletedTask;
    }

    public Task RevokeServiceCredentialsAsync(string purpose, DateTimeOffset revokedAt, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = db.CreateConnection();
        conn.Execute("UPDATE service_credentials SET revoked_at = @revokedAt WHERE purpose = @purpose AND revoked_at IS NULL;",
            new { purpose, revokedAt = revokedAt.ToString("O") });
        return Task.CompletedTask;
    }

    public Task WriteAuditEventAsync(Guid? profileId, Guid? sessionId, string eventType, bool succeeded, string? detail, DateTimeOffset occurredAt, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = db.CreateConnection();
        conn.Execute("""
            INSERT INTO identity_audit_events (profile_id, session_id, event_type, succeeded, detail, occurred_at)
            VALUES (@profileId, @sessionId, @eventType, @succeeded, @detail, @occurredAt);
            """, new { profileId, sessionId, eventType, succeeded = succeeded ? 1 : 0, detail, occurredAt = occurredAt.ToString("O") });
        return Task.CompletedTask;
    }

    private Task<AuthSession?> GetSessionAsync(string predicate, object value, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = db.CreateConnection();
        var row = conn.QueryFirstOrDefault<SessionRow>(SessionSelect + $" WHERE {predicate} LIMIT 1;", new { value });
        return Task.FromResult(row is null ? null : Map(row));
    }

    private const string CredentialSelect = """
        SELECT id AS Id, profile_id AS ProfileId, credential_kind AS Kind,
               normalized_username AS NormalizedUsername, secret_hash AS SecretHash,
               hash_scheme AS HashScheme, hash_version AS HashVersion, security_stamp AS SecurityStamp,
               failed_attempt_count AS FailedAttemptCount, locked_until AS LockedUntil,
               created_at AS CreatedAt, updated_at AS UpdatedAt, last_used_at AS LastUsedAt
        FROM profile_credentials
        """;

    private const string SessionSelect = """
        SELECT id AS Id, profile_id AS ProfileId, active_profile_id AS ActiveProfileId,
               token_hash AS TokenHash, device_id AS DeviceId, device_name AS DeviceName,
               client AS Client, authentication_method AS AuthenticationMethod,
               security_stamp AS SecurityStamp, created_at AS CreatedAt, last_seen_at AS LastSeenAt,
               expires_at AS ExpiresAt, revoked_at AS RevokedAt, revoked_reason AS RevokedReason
        FROM auth_sessions
        """;

    private const string ServiceSelect = """
        SELECT id AS Id, purpose AS Purpose, key_id AS KeyId, token_hash AS TokenHash,
               created_at AS CreatedAt, last_used_at AS LastUsedAt, revoked_at AS RevokedAt
        FROM service_credentials
        """;

    private static object ToParameters(ProfileCredential credential) => new
    {
        credential.Id,
        credential.ProfileId,
        Kind = credential.Kind.ToString(),
        credential.NormalizedUsername,
        credential.SecretHash,
        credential.HashScheme,
        credential.HashVersion,
        credential.SecurityStamp,
        credential.FailedAttemptCount,
        LockedUntil = credential.LockedUntil?.ToString("O"),
        CreatedAt = credential.CreatedAt.ToString("O"),
        UpdatedAt = credential.UpdatedAt.ToString("O"),
        LastUsedAt = credential.LastUsedAt?.ToString("O"),
    };

    private static object ToParameters(AuthSession session) => new
    {
        session.Id,
        session.ProfileId,
        session.ActiveProfileId,
        session.TokenHash,
        session.DeviceId,
        session.DeviceName,
        session.Client,
        session.AuthenticationMethod,
        session.SecurityStamp,
        CreatedAt = session.CreatedAt.ToString("O"),
        LastSeenAt = session.LastSeenAt.ToString("O"),
        ExpiresAt = session.ExpiresAt.ToString("O"),
        RevokedAt = session.RevokedAt?.ToString("O"),
        session.RevokedReason,
    };

    private static ProfileCredential Map(CredentialRow row) => new()
    {
        Id = row.Id, ProfileId = row.ProfileId, Kind = Enum.Parse<ProfileCredentialKind>(row.Kind),
        NormalizedUsername = row.NormalizedUsername, SecretHash = row.SecretHash, HashScheme = row.HashScheme,
        HashVersion = row.HashVersion, SecurityStamp = row.SecurityStamp, FailedAttemptCount = row.FailedAttemptCount,
        LockedUntil = ParseNullable(row.LockedUntil), CreatedAt = DateTimeOffset.Parse(row.CreatedAt),
        UpdatedAt = DateTimeOffset.Parse(row.UpdatedAt), LastUsedAt = ParseNullable(row.LastUsedAt),
    };

    private static AuthSession Map(SessionRow row) => new()
    {
        Id = row.Id, ProfileId = row.ProfileId, ActiveProfileId = row.ActiveProfileId, TokenHash = row.TokenHash,
        DeviceId = row.DeviceId, DeviceName = row.DeviceName, Client = row.Client,
        AuthenticationMethod = row.AuthenticationMethod, SecurityStamp = row.SecurityStamp,
        CreatedAt = DateTimeOffset.Parse(row.CreatedAt), LastSeenAt = DateTimeOffset.Parse(row.LastSeenAt),
        ExpiresAt = DateTimeOffset.Parse(row.ExpiresAt), RevokedAt = ParseNullable(row.RevokedAt), RevokedReason = row.RevokedReason,
    };

    private static PasswordRecoveryCode Map(RecoveryRow row) => new()
    {
        Id = row.Id, ProfileId = row.ProfileId, CodeHash = row.CodeHash,
        CreatedAt = DateTimeOffset.Parse(row.CreatedAt), ExpiresAt = DateTimeOffset.Parse(row.ExpiresAt),
        ConsumedAt = ParseNullable(row.ConsumedAt),
    };

    private static ServiceCredential Map(ServiceRow row) => new()
    {
        Id = row.Id, Purpose = row.Purpose, KeyId = row.KeyId, TokenHash = row.TokenHash,
        CreatedAt = DateTimeOffset.Parse(row.CreatedAt), LastUsedAt = ParseNullable(row.LastUsedAt),
        RevokedAt = ParseNullable(row.RevokedAt),
    };

    private static DateTimeOffset? ParseNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : DateTimeOffset.Parse(value);

    private sealed class CredentialRow
    {
        public Guid Id { get; set; } public Guid ProfileId { get; set; } public string Kind { get; set; } = "";
        public string? NormalizedUsername { get; set; } public string SecretHash { get; set; } = "";
        public string HashScheme { get; set; } = ""; public int HashVersion { get; set; }
        public string SecurityStamp { get; set; } = ""; public int FailedAttemptCount { get; set; }
        public string? LockedUntil { get; set; } public string CreatedAt { get; set; } = "";
        public string UpdatedAt { get; set; } = ""; public string? LastUsedAt { get; set; }
    }

    private sealed class SessionRow
    {
        public Guid Id { get; set; } public Guid ProfileId { get; set; } public Guid ActiveProfileId { get; set; }
        public string TokenHash { get; set; } = ""; public string DeviceId { get; set; } = "";
        public string DeviceName { get; set; } = ""; public string Client { get; set; } = "";
        public string AuthenticationMethod { get; set; } = ""; public string SecurityStamp { get; set; } = "";
        public string CreatedAt { get; set; } = ""; public string LastSeenAt { get; set; } = "";
        public string ExpiresAt { get; set; } = ""; public string? RevokedAt { get; set; }
        public string? RevokedReason { get; set; }
    }

    private sealed class RecoveryRow
    {
        public Guid Id { get; set; } public Guid ProfileId { get; set; } public string CodeHash { get; set; } = "";
        public string CreatedAt { get; set; } = ""; public string ExpiresAt { get; set; } = "";
        public string? ConsumedAt { get; set; }
    }

    private sealed class ServiceRow
    {
        public Guid Id { get; set; } public string Purpose { get; set; } = ""; public string KeyId { get; set; } = "";
        public string TokenHash { get; set; } = ""; public string CreatedAt { get; set; } = "";
        public string? LastUsedAt { get; set; } public string? RevokedAt { get; set; }
    }
}
