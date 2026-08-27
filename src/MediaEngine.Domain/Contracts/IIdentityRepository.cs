using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Contracts;

public interface IIdentityRepository
{
    Task<ProfileCredential?> GetCredentialByUsernameAsync(string normalizedUsername, CancellationToken ct = default);
    Task<ProfileCredential?> GetCredentialAsync(Guid profileId, ProfileCredentialKind kind, CancellationToken ct = default);
    Task UpsertCredentialAsync(ProfileCredential credential, CancellationToken ct = default);
    Task DeleteCredentialAsync(Guid profileId, ProfileCredentialKind kind, CancellationToken ct = default);
    Task UpdateCredentialAttemptAsync(Guid credentialId, int failedAttemptCount, DateTimeOffset? lockedUntil, DateTimeOffset? lastUsedAt, CancellationToken ct = default);
    Task<bool> HasAdministratorPasswordAsync(CancellationToken ct = default);

    Task InsertSessionAsync(AuthSession session, CancellationToken ct = default);
    Task<AuthSession?> GetSessionByTokenHashAsync(string tokenHash, CancellationToken ct = default);
    Task<AuthSession?> GetSessionByIdAsync(Guid sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<AuthSession>> GetSessionsAsync(Guid profileId, CancellationToken ct = default);
    Task TouchSessionAsync(Guid sessionId, DateTimeOffset lastSeenAt, CancellationToken ct = default);
    Task<bool> UpdateActiveProfileAsync(Guid sessionId, Guid activeProfileId, CancellationToken ct = default);
    Task<bool> RevokeSessionAsync(Guid sessionId, DateTimeOffset revokedAt, string reason, CancellationToken ct = default);
    Task<int> RevokeProfileSessionsAsync(Guid profileId, DateTimeOffset revokedAt, string reason, Guid? exceptSessionId = null, CancellationToken ct = default);

    Task InsertRecoveryCodesAsync(IReadOnlyList<PasswordRecoveryCode> codes, CancellationToken ct = default);
    Task<PasswordRecoveryCode?> GetActiveRecoveryCodeAsync(Guid profileId, string codeHash, DateTimeOffset now, CancellationToken ct = default);
    Task<bool> ConsumeRecoveryCodeAsync(Guid codeId, DateTimeOffset consumedAt, CancellationToken ct = default);
    Task DeleteRecoveryCodesAsync(Guid profileId, CancellationToken ct = default);

    Task<ServiceCredential?> GetActiveServiceCredentialAsync(string purpose, CancellationToken ct = default);
    Task<ServiceCredential?> GetServiceCredentialByHashAsync(string tokenHash, CancellationToken ct = default);
    Task InsertServiceCredentialAsync(ServiceCredential credential, CancellationToken ct = default);
    Task RevokeServiceCredentialsAsync(string purpose, DateTimeOffset revokedAt, CancellationToken ct = default);

    Task WriteAuditEventAsync(Guid? profileId, Guid? sessionId, string eventType, bool succeeded, string? detail, DateTimeOffset occurredAt, CancellationToken ct = default);
}
