using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Entities;

namespace MediaEngine.Identity.Contracts;

public sealed record SessionIssueResult(
    AuthSession Session,
    Profile Profile,
    Profile ActiveProfile,
    string PlaintextToken,
    IReadOnlyList<string> RecoveryCodes);

public sealed record SessionValidationResult(AuthSession Session, Profile Profile, Profile ActiveProfile);

public sealed record AuthenticationAttemptResult(
    bool Succeeded,
    bool LockedOut,
    string? Error,
    SessionIssueResult? IssuedSession);

public interface IFirstPartyIdentityService
{
    Task<bool> IsAdministratorConfiguredAsync(CancellationToken ct = default);
    Task<SessionIssueResult> BootstrapAdministratorAsync(string username, string password, string displayName, string deviceId, string deviceName, string client, CancellationToken ct = default);
    Task<AuthenticationAttemptResult> AuthenticatePasswordAsync(string username, string password, string deviceId, string deviceName, string client, CancellationToken ct = default);
    Task<AuthenticationAttemptResult> AuthenticatePinAsync(Guid profileId, string pin, string deviceId, string deviceName, string client, CancellationToken ct = default);
    Task<SessionIssueResult> CreateExternalSessionAsync(Guid profileId, string provider, string deviceId, string deviceName, string client, CancellationToken ct = default);
    Task<SessionValidationResult?> ValidateSessionAsync(string plaintextToken, bool touch = true, CancellationToken ct = default);
    Task<IReadOnlyList<AuthSession>> GetSessionsAsync(Guid profileId, CancellationToken ct = default);
    Task<bool> RevokeSessionAsync(Guid sessionId, string reason, CancellationToken ct = default);
    Task<int> RevokeOtherSessionsAsync(Guid profileId, Guid currentSessionId, string reason, CancellationToken ct = default);
    Task ChangePasswordAsync(Guid profileId, string currentPassword, string newPassword, Guid? currentSessionId = null, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ResetPasswordWithRecoveryCodeAsync(string username, string recoveryCode, string newPassword, CancellationToken ct = default);
    Task<IReadOnlyList<string>> RegenerateRecoveryCodesAsync(Guid profileId, CancellationToken ct = default);
    Task SetProfilePinAsync(Guid profileId, string? pin, CancellationToken ct = default);
    Task<SessionValidationResult> SwitchActiveProfileAsync(string sessionToken, Guid targetProfileId, string? secret, CancellationToken ct = default);
    Task<bool> ValidateServiceCredentialAsync(string plaintextToken, CancellationToken ct = default);
}
