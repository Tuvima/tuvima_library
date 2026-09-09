using System.Collections.Frozen;
using Dapper;
using MediaEngine.Domain.Authorization;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;
using Microsoft.Data.Sqlite;

namespace MediaEngine.Storage;

public sealed class ApplicationRepository(IDatabaseConnection database) : IApplicationRepository
{
    public Task<Application?> GetApplicationAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var connection = database.CreateConnection();
        var row = connection.QueryFirstOrDefault<ApplicationRow>(
            $"{ApplicationSelect} WHERE id = @id;",
            new { id = GuidSql.ToBlob(id) });
        return Task.FromResult(row is null ? null : MapApplication(row));
    }

    public Task<Application?> GetApplicationByClientIdAsync(string clientId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return Task.FromResult<Application?>(null);
        }

        using var connection = database.CreateConnection();
        var row = connection.QueryFirstOrDefault<ApplicationRow>(
            ApplicationSelect + """
             WHERE id = (
                 SELECT application_id
                   FROM application_client_bindings
                  WHERE client_id = @clientId COLLATE BINARY)
               AND is_enabled = 1
               AND application_type = 'UserClient'
             LIMIT 1;
            """,
            new { clientId });
        return Task.FromResult(row is null ? null : MapApplication(row));
    }

    public Task<IReadOnlyList<Application>> GetApplicationsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var connection = database.CreateConnection();
        var values = connection.Query<ApplicationRow>(
                $"{ApplicationSelect} ORDER BY name COLLATE NOCASE, id;")
            .Select(MapApplication)
            .ToArray();
        return Task.FromResult<IReadOnlyList<Application>>(Array.AsReadOnly(values));
    }

    public Task<IReadOnlySet<string>> GetApplicationClientBindingsAsync(
        Guid applicationId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var connection = database.CreateConnection();
        var values = connection.Query<string>(
                """
                SELECT client_id
                  FROM application_client_bindings
                 WHERE application_id = @applicationId
                 ORDER BY client_id COLLATE BINARY;
                """,
                new { applicationId = GuidSql.ToBlob(applicationId) })
            .ToFrozenSet(StringComparer.Ordinal);
        return Task.FromResult<IReadOnlySet<string>>(values);
    }

    public Task<IReadOnlySet<ApplicationPermissionId>> GetApplicationPermissionsAsync(
        Guid applicationId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var connection = database.CreateConnection();
        var values = connection.Query<string>(
                "SELECT permission_id FROM application_permission_grants WHERE application_id = @applicationId ORDER BY permission_id;",
                new { applicationId = GuidSql.ToBlob(applicationId) })
            .Select(value => new ApplicationPermissionId(value))
            .ToFrozenSet();
        return Task.FromResult<IReadOnlySet<ApplicationPermissionId>>(values);
    }

    public Task<ApplicationCredentialIdentity?> FindApplicationCredentialAsync(
        string hash,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        ct.ThrowIfCancellationRequested();
        using var connection = database.CreateConnection();
        var row = connection.QueryFirstOrDefault<CredentialApplicationRow>(
            CredentialApplicationSelect + """
             WHERE c.credential_hash = @hash
               AND c.revoked_at IS NULL
               AND (c.expires_at IS NULL OR c.expires_at > @now)
               AND a.is_enabled = 1
             LIMIT 1;
            """,
            new { hash, now = Iso(now) });
        return Task.FromResult(row is null
            ? null
            : new ApplicationCredentialIdentity(
                MapApplication(row),
                MapCredential(row),
                ClientAuthorizationScopes(row.PermissionIds)));
    }

    public Task<IReadOnlyList<ApplicationCredential>> GetApplicationCredentialsAsync(
        Guid applicationId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var connection = database.CreateConnection();
        var values = connection.Query<CredentialRow>(
                $"{CredentialSelect} WHERE application_id = @applicationId ORDER BY created_at DESC, id;",
                new { applicationId = GuidSql.ToBlob(applicationId) })
            .Select(MapCredential)
            .ToArray();
        return Task.FromResult<IReadOnlyList<ApplicationCredential>>(Array.AsReadOnly(values));
    }

    public Task InsertApplicationAsync(
        Application application,
        IReadOnlySet<ApplicationPermissionId> permissions,
        CancellationToken ct = default) =>
        database.ExecuteWriteAsync((connection, transaction, token) =>
        {
            token.ThrowIfCancellationRequested();
            connection.Execute(
                """
                INSERT INTO applications(
                    id, name, description, application_type, is_enabled, is_administrator,
                    authorization_version, created_at, updated_at, last_used_at)
                VALUES(
                    @Id, @Name, @Description, @ApplicationType, @IsEnabled, @IsAdministrator,
                    @AuthorizationVersion, @CreatedAt, @UpdatedAt, @LastUsedAt);
                """,
                ApplicationParameters(application),
                transaction);
            InsertPermissions(connection, transaction, application.Id, permissions, application.CreatedAt);
        }, ct);

    public Task UpdateApplicationAsync(Application application, CancellationToken ct = default) =>
        database.ExecuteWriteAsync((connection, transaction, token) =>
        {
            token.ThrowIfCancellationRequested();
            var changed = connection.Execute(
                """
                UPDATE applications
                   SET name = @Name,
                       description = @Description,
                       application_type = @ApplicationType,
                       is_enabled = @IsEnabled,
                       is_administrator = @IsAdministrator,
                       authorization_version = authorization_version + 1,
                       updated_at = @UpdatedAt
                 WHERE id = @Id;
                """,
                ApplicationParameters(application),
                transaction);
            if (changed != 1)
            {
                throw new InvalidOperationException("Application not found.");
            }
        }, ct);

    public Task<bool> DeleteApplicationAsync(
        Guid applicationId,
        DateTimeOffset deletedAt,
        CancellationToken ct = default) =>
        database.ExecuteWriteAsync((connection, transaction, token) =>
        {
            token.ThrowIfCancellationRequested();
            return connection.Execute(
                "DELETE FROM applications WHERE id = @applicationId;",
                new { applicationId = GuidSql.ToBlob(applicationId) },
                transaction) == 1;
        }, ct);

    public Task ReplacePermissionsAsync(
        Guid applicationId,
        IReadOnlySet<ApplicationPermissionId> permissions,
        DateTimeOffset changedAt,
        CancellationToken ct = default) =>
        database.ExecuteWriteAsync((connection, transaction, token) =>
        {
            token.ThrowIfCancellationRequested();
            var applicationIdBlob = GuidSql.ToBlob(applicationId);
            if (connection.ExecuteScalar<long>(
                    "SELECT COUNT(*) FROM applications WHERE id = @applicationId;",
                    new { applicationId = applicationIdBlob },
                    transaction) != 1)
            {
                throw new InvalidOperationException("Application not found.");
            }

            connection.Execute(
                "DELETE FROM application_permission_grants WHERE application_id = @applicationId;",
                new { applicationId = applicationIdBlob },
                transaction);
            InsertPermissions(connection, transaction, applicationId, permissions, changedAt);
            IncrementAuthorizationVersion(connection, transaction, applicationId, changedAt);
        }, ct);

    public async Task ReplaceClientBindingsAsync(
        Guid applicationId,
        IReadOnlySet<string> clientIds,
        DateTimeOffset changedAt,
        CancellationToken ct = default)
    {
        try
        {
            await database.ExecuteWriteAsync((connection, transaction, token) =>
            {
                ArgumentNullException.ThrowIfNull(clientIds);
                token.ThrowIfCancellationRequested();
                var applicationIdBlob = GuidSql.ToBlob(applicationId);
                if (connection.ExecuteScalar<long>(
                        """
                        SELECT COUNT(*)
                          FROM applications
                         WHERE id = @applicationId
                           AND application_type = 'UserClient';
                        """,
                        new { applicationId = applicationIdBlob },
                        transaction) != 1)
                {
                    throw new InvalidOperationException(
                        "Client bindings require an existing UserClient application.");
                }

                connection.Execute(
                    "DELETE FROM application_client_bindings WHERE application_id = @applicationId;",
                    new { applicationId = applicationIdBlob },
                    transaction);
                foreach (var clientId in clientIds.Order(StringComparer.Ordinal))
                {
                    connection.Execute(
                        """
                        INSERT INTO application_client_bindings(application_id, client_id, created_at)
                        VALUES(@applicationId, @clientId, @createdAt);
                        """,
                        new
                        {
                            applicationId = applicationIdBlob,
                            clientId,
                            createdAt = Iso(changedAt),
                        },
                        transaction);
                }
                IncrementAuthorizationVersion(connection, transaction, applicationId, changedAt);
            }, ct).ConfigureAwait(false);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException(
                "A client identifier is already bound to another application.",
                exception);
        }
    }

    public Task InsertCredentialAsync(ApplicationCredential credential, CancellationToken ct = default) =>
        database.ExecuteWriteAsync((connection, transaction, token) =>
        {
            token.ThrowIfCancellationRequested();
            var inserted = connection.Execute(
                """
                INSERT INTO application_credentials(
                    id, application_id, name, credential_hash, hash_scheme,
                    created_at, expires_at, last_used_at, revoked_at)
                SELECT
                    @Id, @ApplicationId, @Name, @CredentialHash, @HashScheme,
                    @CreatedAt, @ExpiresAt, @LastUsedAt, @RevokedAt
                  FROM applications
                 WHERE id = @ApplicationId AND is_enabled = 1;
                """,
                CredentialParameters(credential),
                transaction);
            if (inserted != 1)
            {
                throw new InvalidOperationException("An enabled application is required to issue a credential.");
            }

            IncrementAuthorizationVersion(connection, transaction, credential.ApplicationId, credential.CreatedAt);
        }, ct);

    public Task<bool> RevokeCredentialAsync(
        Guid applicationId,
        Guid credentialId,
        DateTimeOffset revokedAt,
        CancellationToken ct = default) =>
        database.ExecuteWriteAsync((connection, transaction, token) =>
        {
            token.ThrowIfCancellationRequested();
            var changed = connection.Execute(
                """
                UPDATE application_credentials
                   SET revoked_at = @revokedAt
                 WHERE id = @credentialId
                   AND application_id = @applicationId
                   AND revoked_at IS NULL;
                """,
                new
                {
                    applicationId = GuidSql.ToBlob(applicationId),
                    credentialId = GuidSql.ToBlob(credentialId),
                    revokedAt = Iso(revokedAt),
                },
                transaction) == 1;
            if (changed)
            {
                IncrementAuthorizationVersion(connection, transaction, applicationId, revokedAt);
            }

            return changed;
        }, ct);

    public Task<bool> RotateCredentialAsync(
        Guid applicationId,
        Guid priorCredentialId,
        ApplicationCredential replacement,
        DateTimeOffset rotatedAt,
        CancellationToken ct = default) =>
        database.ExecuteWriteAsync((connection, transaction, token) =>
        {
            token.ThrowIfCancellationRequested();
            if (replacement.ApplicationId != applicationId)
            {
                throw new ArgumentException("The replacement credential must belong to the same application.", nameof(replacement));
            }

            var applicationIdBlob = GuidSql.ToBlob(applicationId);
            var revoked = connection.Execute(
                """
                UPDATE application_credentials
                   SET revoked_at = @rotatedAt
                 WHERE id = @priorCredentialId
                   AND application_id = @applicationId
                   AND revoked_at IS NULL
                   AND EXISTS (
                       SELECT 1 FROM applications
                        WHERE id = @applicationId AND is_enabled = 1);
                """,
                new
                {
                    applicationId = applicationIdBlob,
                    priorCredentialId = GuidSql.ToBlob(priorCredentialId),
                    rotatedAt = Iso(rotatedAt),
                },
                transaction);
            if (revoked != 1)
            {
                return false;
            }

            connection.Execute(InsertCredentialSql, CredentialParameters(replacement), transaction);
            IncrementAuthorizationVersion(connection, transaction, applicationId, rotatedAt);
            return true;
        }, ct);

    public Task TouchCredentialUsageAsync(
        Guid applicationId,
        Guid credentialId,
        DateTimeOffset usedAt,
        CancellationToken ct = default) =>
        database.ExecuteWriteAsync((connection, transaction, token) =>
        {
            token.ThrowIfCancellationRequested();
            var parameters = new
            {
                applicationId = GuidSql.ToBlob(applicationId),
                credentialId = GuidSql.ToBlob(credentialId),
                usedAt = Iso(usedAt),
            };
            var changed = connection.Execute(
                """
                UPDATE application_credentials
                   SET last_used_at = CASE
                       WHEN last_used_at IS NULL OR last_used_at < @usedAt THEN @usedAt
                       ELSE last_used_at END
                 WHERE id = @credentialId
                   AND application_id = @applicationId
                   AND revoked_at IS NULL
                   AND (expires_at IS NULL OR expires_at > @usedAt)
                   AND EXISTS (
                       SELECT 1 FROM applications
                        WHERE id = @applicationId AND is_enabled = 1);
                """,
                parameters,
                transaction);
            if (changed == 1)
            {
                connection.Execute(
                    """
                    UPDATE applications
                       SET last_used_at = CASE
                           WHEN last_used_at IS NULL OR last_used_at < @usedAt THEN @usedAt
                           ELSE last_used_at END
                     WHERE id = @applicationId;
                    """,
                    parameters,
                    transaction);
            }
        }, ct);

    private static void InsertPermissions(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid applicationId,
        IReadOnlySet<ApplicationPermissionId> permissions,
        DateTimeOffset grantedAt)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        foreach (var permission in permissions.OrderBy(value => value.Value, StringComparer.Ordinal))
        {
            connection.Execute(
                """
                INSERT INTO application_permission_grants(application_id, permission_id, granted_at)
                VALUES(@applicationId, @permissionId, @grantedAt);
                """,
                new
                {
                    applicationId = GuidSql.ToBlob(applicationId),
                    permissionId = permission.Value,
                    grantedAt = Iso(grantedAt),
                },
                transaction);
        }
    }

    private static void IncrementAuthorizationVersion(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid applicationId,
        DateTimeOffset changedAt) =>
        connection.Execute(
            """
            UPDATE applications
               SET authorization_version = authorization_version + 1,
                   updated_at = @changedAt
             WHERE id = @applicationId;
            """,
            new { applicationId = GuidSql.ToBlob(applicationId), changedAt = Iso(changedAt) },
            transaction);

    private static object ApplicationParameters(Application application) => new
    {
        Id = GuidSql.ToBlob(application.Id),
        application.Name,
        application.Description,
        ApplicationType = application.ApplicationType.ToString(),
        IsEnabled = application.IsEnabled ? 1 : 0,
        IsAdministrator = application.IsAdministrator ? 1 : 0,
        application.AuthorizationVersion,
        CreatedAt = Iso(application.CreatedAt),
        UpdatedAt = Iso(application.UpdatedAt),
        LastUsedAt = application.LastUsedAt is null ? null : Iso(application.LastUsedAt.Value),
    };

    private static object CredentialParameters(ApplicationCredential credential) => new
    {
        Id = GuidSql.ToBlob(credential.Id),
        ApplicationId = GuidSql.ToBlob(credential.ApplicationId),
        credential.Name,
        credential.CredentialHash,
        credential.HashScheme,
        CreatedAt = Iso(credential.CreatedAt),
        ExpiresAt = credential.ExpiresAt is null ? null : Iso(credential.ExpiresAt.Value),
        LastUsedAt = credential.LastUsedAt is null ? null : Iso(credential.LastUsedAt.Value),
        RevokedAt = credential.RevokedAt is null ? null : Iso(credential.RevokedAt.Value),
    };

    private static Application MapApplication(ApplicationRow row) => new()
    {
        Id = row.Id,
        Name = row.Name,
        Description = row.Description,
        ApplicationType = Enum.Parse<ApplicationType>(row.ApplicationType),
        IsEnabled = row.IsEnabled,
        IsAdministrator = row.IsAdministrator,
        AuthorizationVersion = row.AuthorizationVersion,
        CreatedAt = Parse(row.CreatedAt),
        UpdatedAt = Parse(row.UpdatedAt),
        LastUsedAt = ParseNullable(row.LastUsedAt),
    };

    private static Application MapApplication(CredentialApplicationRow row) => new()
    {
        Id = row.ApplicationId,
        Name = row.ApplicationName,
        Description = row.ApplicationDescription,
        ApplicationType = Enum.Parse<ApplicationType>(row.ApplicationType),
        IsEnabled = row.ApplicationIsEnabled,
        IsAdministrator = row.ApplicationIsAdministrator,
        AuthorizationVersion = row.ApplicationAuthorizationVersion,
        CreatedAt = Parse(row.ApplicationCreatedAt),
        UpdatedAt = Parse(row.ApplicationUpdatedAt),
        LastUsedAt = ParseNullable(row.ApplicationLastUsedAt),
    };

    private static ApplicationCredential MapCredential(CredentialRow row) => new()
    {
        Id = row.Id,
        ApplicationId = row.ApplicationId,
        Name = row.Name,
        CredentialHash = row.CredentialHash,
        HashScheme = row.HashScheme,
        CreatedAt = Parse(row.CreatedAt),
        ExpiresAt = ParseNullable(row.ExpiresAt),
        LastUsedAt = ParseNullable(row.LastUsedAt),
        RevokedAt = ParseNullable(row.RevokedAt),
    };

    private static ApplicationCredential MapCredential(CredentialApplicationRow row) => new()
    {
        Id = row.CredentialId,
        ApplicationId = row.ApplicationId,
        Name = row.CredentialName,
        CredentialHash = row.CredentialHash,
        HashScheme = row.HashScheme,
        CreatedAt = Parse(row.CredentialCreatedAt),
        ExpiresAt = ParseNullable(row.CredentialExpiresAt),
        LastUsedAt = ParseNullable(row.CredentialLastUsedAt),
        RevokedAt = ParseNullable(row.CredentialRevokedAt),
    };

    private static string Iso(DateTimeOffset value) =>
        value.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Parse(value);

    private const string ApplicationSelect = """
        SELECT id AS Id, name AS Name, description AS Description,
               application_type AS ApplicationType, is_enabled AS IsEnabled,
               is_administrator AS IsAdministrator,
               authorization_version AS AuthorizationVersion,
               created_at AS CreatedAt, updated_at AS UpdatedAt, last_used_at AS LastUsedAt
          FROM applications
        """;

    private const string CredentialSelect = """
        SELECT id AS Id, application_id AS ApplicationId, name AS Name,
               credential_hash AS CredentialHash, hash_scheme AS HashScheme,
               created_at AS CreatedAt, expires_at AS ExpiresAt,
               last_used_at AS LastUsedAt, revoked_at AS RevokedAt
          FROM application_credentials
        """;

    private const string CredentialApplicationSelect = """
        SELECT c.id AS CredentialId, c.application_id AS ApplicationId,
               c.name AS CredentialName, c.credential_hash AS CredentialHash,
               c.hash_scheme AS HashScheme, c.created_at AS CredentialCreatedAt,
               c.expires_at AS CredentialExpiresAt, c.last_used_at AS CredentialLastUsedAt,
               c.revoked_at AS CredentialRevokedAt, a.name AS ApplicationName,
               a.description AS ApplicationDescription, a.application_type AS ApplicationType,
               a.is_enabled AS ApplicationIsEnabled,
               a.is_administrator AS ApplicationIsAdministrator,
               a.authorization_version AS ApplicationAuthorizationVersion,
               a.created_at AS ApplicationCreatedAt, a.updated_at AS ApplicationUpdatedAt,
               a.last_used_at AS ApplicationLastUsedAt,
               COALESCE((
                   SELECT group_concat(grant.permission_id, ' ')
                   FROM (
                       SELECT permission_id
                       FROM application_permission_grants
                       WHERE application_id=c.application_id
                       ORDER BY permission_id
                   ) grant
               ), '') AS PermissionIds
          FROM application_credentials c
          JOIN applications a ON a.id = c.application_id
        """;

    private const string InsertCredentialSql = """
        INSERT INTO application_credentials(
            id, application_id, name, credential_hash, hash_scheme,
            created_at, expires_at, last_used_at, revoked_at)
        VALUES(
            @Id, @ApplicationId, @Name, @CredentialHash, @HashScheme,
            @CreatedAt, @ExpiresAt, @LastUsedAt, @RevokedAt);
        """;

    private sealed class ApplicationRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string ApplicationType { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public bool IsAdministrator { get; set; }
        public long AuthorizationVersion { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
        public string UpdatedAt { get; set; } = string.Empty;
        public string? LastUsedAt { get; set; }
    }

    private sealed class CredentialRow
    {
        public Guid Id { get; set; }
        public Guid ApplicationId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string CredentialHash { get; set; } = string.Empty;
        public string HashScheme { get; set; } = string.Empty;
        public string CreatedAt { get; set; } = string.Empty;
        public string? ExpiresAt { get; set; }
        public string? LastUsedAt { get; set; }
        public string? RevokedAt { get; set; }
    }

    private sealed class CredentialApplicationRow
    {
        public Guid CredentialId { get; set; }
        public Guid ApplicationId { get; set; }
        public string CredentialName { get; set; } = string.Empty;
        public string CredentialHash { get; set; } = string.Empty;
        public string HashScheme { get; set; } = string.Empty;
        public string CredentialCreatedAt { get; set; } = string.Empty;
        public string? CredentialExpiresAt { get; set; }
        public string? CredentialLastUsedAt { get; set; }
        public string? CredentialRevokedAt { get; set; }
        public string ApplicationName { get; set; } = string.Empty;
        public string? ApplicationDescription { get; set; }
        public string ApplicationType { get; set; } = string.Empty;
        public bool ApplicationIsEnabled { get; set; }
        public bool ApplicationIsAdministrator { get; set; }
        public long ApplicationAuthorizationVersion { get; set; }
        public string ApplicationCreatedAt { get; set; } = string.Empty;
        public string ApplicationUpdatedAt { get; set; } = string.Empty;
        public string? ApplicationLastUsedAt { get; set; }
        public string PermissionIds { get; set; } = string.Empty;
    }

    private static IReadOnlySet<ApplicationPermissionId> ClientAuthorizationScopes(string value) =>
        value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(permission => new ApplicationPermissionId(permission))
            .ToFrozenSet();
}
