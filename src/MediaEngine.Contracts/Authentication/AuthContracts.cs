using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.Authentication;

public sealed record AuthBootstrapStatusResponse(
    [property: JsonPropertyName("administrator_configured")] bool AdministratorConfigured);

public sealed class BootstrapAdministratorRequest
{
    [JsonPropertyName("email")] public string Email { get; init; } = string.Empty;
    [JsonPropertyName("password")] public string Password { get; init; } = string.Empty;
    [JsonPropertyName("display_name")] public string DisplayName { get; init; } = "Administrator";
    [JsonPropertyName("device_id")] public string DeviceId { get; init; } = string.Empty;
    [JsonPropertyName("device_name")] public string DeviceName { get; init; } = string.Empty;
    [JsonPropertyName("client")] public string Client { get; init; } = "Dashboard";
}

public sealed class LocalLoginRequest
{
    [JsonPropertyName("email")] public string? Email { get; init; }
    [JsonPropertyName("password")] public string? Password { get; init; }
    [JsonPropertyName("profile_id")] public Guid? ProfileId { get; init; }
    [JsonPropertyName("pin")] public string? Pin { get; init; }
    [JsonPropertyName("device_id")] public string DeviceId { get; init; } = string.Empty;
    [JsonPropertyName("device_name")] public string DeviceName { get; init; } = string.Empty;
    [JsonPropertyName("client")] public string Client { get; init; } = "Dashboard";
}

public sealed class ExternalSessionRequest
{
    [JsonPropertyName("provider")] public string Provider { get; init; } = string.Empty;
    [JsonPropertyName("issuer")] public string Issuer { get; init; } = string.Empty;
    [JsonPropertyName("subject")] public string Subject { get; init; } = string.Empty;
    [JsonPropertyName("device_id")] public string DeviceId { get; init; } = string.Empty;
    [JsonPropertyName("device_name")] public string DeviceName { get; init; } = string.Empty;
    [JsonPropertyName("client")] public string Client { get; init; } = "Dashboard";
}

public sealed class AuthSessionResponse
{
    [JsonPropertyName("session_id")] public Guid SessionId { get; init; }
    [JsonPropertyName("session_token")] public string SessionToken { get; init; } = string.Empty;
    [JsonPropertyName("account_id")] public Guid AccountId { get; init; }
    [JsonPropertyName("active_profile_id")] public Guid ActiveProfileId { get; init; }
    [JsonPropertyName("display_name")] public string DisplayName { get; init; } = string.Empty;
    [JsonPropertyName("role")] public string Role { get; init; } = string.Empty;
    [JsonPropertyName("authentication_method")] public string AuthenticationMethod { get; init; } = string.Empty;
    [JsonPropertyName("expires_at")] public DateTimeOffset ExpiresAt { get; init; }
    [JsonPropertyName("recovery_codes")] public IReadOnlyList<string> RecoveryCodes { get; init; } = [];
}

public sealed class SessionValidationResponse
{
    [JsonPropertyName("session_id")] public Guid SessionId { get; init; }
    [JsonPropertyName("account_id")] public Guid AccountId { get; init; }
    [JsonPropertyName("active_profile_id")] public Guid ActiveProfileId { get; init; }
    [JsonPropertyName("display_name")] public string DisplayName { get; init; } = string.Empty;
    [JsonPropertyName("role")] public string Role { get; init; } = string.Empty;
    [JsonPropertyName("authentication_method")] public string AuthenticationMethod { get; init; } = string.Empty;
    [JsonPropertyName("expires_at")] public DateTimeOffset ExpiresAt { get; init; }
}

public sealed class DeviceSessionResponse
{
    [JsonPropertyName("id")] public Guid Id { get; init; }
    [JsonPropertyName("account_id")] public Guid AccountId { get; init; }
    [JsonPropertyName("active_profile_id")] public Guid ActiveProfileId { get; init; }
    [JsonPropertyName("device_id")] public string DeviceId { get; init; } = string.Empty;
    [JsonPropertyName("device_name")] public string DeviceName { get; init; } = string.Empty;
    [JsonPropertyName("client")] public string Client { get; init; } = string.Empty;
    [JsonPropertyName("authentication_method")] public string AuthenticationMethod { get; init; } = string.Empty;
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("last_seen_at")] public DateTimeOffset LastSeenAt { get; init; }
    [JsonPropertyName("expires_at")] public DateTimeOffset ExpiresAt { get; init; }
    [JsonPropertyName("revoked_at")] public DateTimeOffset? RevokedAt { get; init; }
}

public sealed class ChangePasswordRequest
{
    [JsonPropertyName("current_password")] public string CurrentPassword { get; init; } = string.Empty;
    [JsonPropertyName("new_password")] public string NewPassword { get; init; } = string.Empty;
}

public sealed class RecoverPasswordRequest
{
    [JsonPropertyName("email")] public string Email { get; init; } = string.Empty;
    [JsonPropertyName("recovery_code")] public string RecoveryCode { get; init; } = string.Empty;
    [JsonPropertyName("new_password")] public string NewPassword { get; init; } = string.Empty;
}

public sealed class SetProfilePinRequest
{
    [JsonPropertyName("pin")] public string Pin { get; init; } = string.Empty;
}

public sealed record RegenerateRecoveryCodesRequest(
    [property: JsonPropertyName("current_password")] string CurrentPassword);

public sealed record BeginPasswordResetRequest([property: JsonPropertyName("email")] string Email);
public sealed record BeginPasswordResetResponse([property: JsonPropertyName("token")] string? Token);
public sealed record ResetPasswordTokenRequest(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("new_password")] string NewPassword);

public sealed record PasskeyOptionsResponse(
    [property: JsonPropertyName("options_json")] string OptionsJson,
    [property: JsonPropertyName("state")] string State);
public sealed record BeginPasskeyLoginRequest([property: JsonPropertyName("email")] string? Email);
public sealed record CompletePasskeyLoginRequest(
    [property: JsonPropertyName("credential_json")] string CredentialJson,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("device_id")] string DeviceId,
    [property: JsonPropertyName("device_name")] string DeviceName);
public sealed record CompletePasskeyRegistrationRequest(
    [property: JsonPropertyName("credential_json")] string CredentialJson,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("name")] string Name);
public sealed record CompletePasskeyElevationRequest(
    [property: JsonPropertyName("credential_json")] string CredentialJson,
    [property: JsonPropertyName("state")] string State);
public sealed record PasskeyCredentialResponse(
    [property: JsonPropertyName("credential_id")] string CredentialId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("backed_up")] bool BackedUp);

public sealed class ElevateAdministratorRequest
{
    [JsonPropertyName("secret")] public string Secret { get; init; } = string.Empty;
}

public sealed record AdministratorElevationResponse(
    [property: JsonPropertyName("elevated")] bool Elevated,
    [property: JsonPropertyName("expires_at")] DateTimeOffset? ExpiresAt,
    [property: JsonPropertyName("error")] string? Error = null);

public sealed class AccountResponse
{
    [JsonPropertyName("id")] public Guid Id { get; init; }
    [JsonPropertyName("email")] public string? Email { get; init; }
    [JsonPropertyName("is_local_only")] public bool IsLocalOnly { get; init; }
    [JsonPropertyName("is_enabled")] public bool IsEnabled { get; init; }
    [JsonPropertyName("profile_ids")] public IReadOnlyList<Guid> ProfileIds { get; init; } = [];
    [JsonPropertyName("default_profile_id")] public Guid? DefaultProfileId { get; init; }
}

public sealed class CreateAccountRequest
{
    [JsonPropertyName("email")] public string? Email { get; init; }
    [JsonPropertyName("profile_ids")] public IReadOnlyList<Guid> ProfileIds { get; init; } = [];
    [JsonPropertyName("default_profile_id")] public Guid? DefaultProfileId { get; init; }
}
public sealed record SetAccountProfileGrantRequest(
    [property: JsonPropertyName("is_default")] bool IsDefault = false);

public sealed class CreateAccountInvitationRequest
{
    [JsonPropertyName("email")] public string Email { get; init; } = string.Empty;
    [JsonPropertyName("profile_ids")] public IReadOnlyList<Guid> ProfileIds { get; init; } = [];
    [JsonPropertyName("default_profile_id")] public Guid? DefaultProfileId { get; init; }
}
public sealed record AccountInvitationResponse(
    [property: JsonPropertyName("account_id")] Guid AccountId,
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt);
public sealed record AcceptAccountInvitationRequest(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("password")] string Password,
    [property: JsonPropertyName("device_id")] string DeviceId,
    [property: JsonPropertyName("device_name")] string DeviceName);

public sealed class LinkAccountExternalLoginRequest
{
    [JsonPropertyName("provider")] public string Provider { get; init; } = string.Empty;
    [JsonPropertyName("issuer")] public string Issuer { get; init; } = string.Empty;
    [JsonPropertyName("subject")] public string Subject { get; init; } = string.Empty;
    [JsonPropertyName("email")] public string? Email { get; init; }
    [JsonPropertyName("display_name")] public string? DisplayName { get; init; }
}

public sealed class AccountExternalLoginDto
{
    [JsonPropertyName("id")] public Guid Id { get; init; }
    [JsonPropertyName("account_id")] public Guid AccountId { get; init; }
    [JsonPropertyName("provider")] public string Provider { get; init; } = string.Empty;
    [JsonPropertyName("issuer")] public string Issuer { get; init; } = string.Empty;
    [JsonPropertyName("subject")] public string Subject { get; init; } = string.Empty;
    [JsonPropertyName("email")] public string? Email { get; init; }
    [JsonPropertyName("display_name")] public string? DisplayName { get; init; }
    [JsonPropertyName("linked_at")] public DateTimeOffset LinkedAt { get; init; }
    [JsonPropertyName("last_login_at")] public DateTimeOffset? LastLoginAt { get; init; }
}

public sealed class SwitchProfileRequest
{
    [JsonPropertyName("profile_id")] public Guid ProfileId { get; init; }
    [JsonPropertyName("secret")] public string? Secret { get; init; }
}

public sealed record RecoveryCodesResponse(
    [property: JsonPropertyName("recovery_codes")] IReadOnlyList<string> RecoveryCodes);

public sealed record IntercomTokenResponse(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt);

public sealed record DashboardServiceCredentialBundle(
    [property: JsonPropertyName("key_id")] string KeyId,
    [property: JsonPropertyName("protected_token")] string ProtectedToken,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);
