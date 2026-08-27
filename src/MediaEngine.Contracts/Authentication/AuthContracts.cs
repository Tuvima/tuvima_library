using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.Authentication;

public sealed record AuthBootstrapStatusResponse(
    [property: JsonPropertyName("administrator_configured")] bool AdministratorConfigured);

public sealed class BootstrapAdministratorRequest
{
    [JsonPropertyName("username")] public string Username { get; init; } = string.Empty;
    [JsonPropertyName("password")] public string Password { get; init; } = string.Empty;
    [JsonPropertyName("display_name")] public string DisplayName { get; init; } = "Owner";
    [JsonPropertyName("device_id")] public string DeviceId { get; init; } = string.Empty;
    [JsonPropertyName("device_name")] public string DeviceName { get; init; } = string.Empty;
    [JsonPropertyName("client")] public string Client { get; init; } = "Dashboard";
}

public sealed class LocalLoginRequest
{
    [JsonPropertyName("username")] public string? Username { get; init; }
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
    [JsonPropertyName("subject")] public string Subject { get; init; } = string.Empty;
    [JsonPropertyName("device_id")] public string DeviceId { get; init; } = string.Empty;
    [JsonPropertyName("device_name")] public string DeviceName { get; init; } = string.Empty;
    [JsonPropertyName("client")] public string Client { get; init; } = "Dashboard";
}

public sealed class AuthSessionResponse
{
    [JsonPropertyName("session_id")] public Guid SessionId { get; init; }
    [JsonPropertyName("session_token")] public string SessionToken { get; init; } = string.Empty;
    [JsonPropertyName("profile_id")] public Guid ProfileId { get; init; }
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
    [JsonPropertyName("profile_id")] public Guid ProfileId { get; init; }
    [JsonPropertyName("active_profile_id")] public Guid ActiveProfileId { get; init; }
    [JsonPropertyName("display_name")] public string DisplayName { get; init; } = string.Empty;
    [JsonPropertyName("role")] public string Role { get; init; } = string.Empty;
    [JsonPropertyName("authentication_method")] public string AuthenticationMethod { get; init; } = string.Empty;
    [JsonPropertyName("expires_at")] public DateTimeOffset ExpiresAt { get; init; }
}

public sealed class DeviceSessionResponse
{
    [JsonPropertyName("id")] public Guid Id { get; init; }
    [JsonPropertyName("profile_id")] public Guid ProfileId { get; init; }
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
    [JsonPropertyName("username")] public string Username { get; init; } = string.Empty;
    [JsonPropertyName("recovery_code")] public string RecoveryCode { get; init; } = string.Empty;
    [JsonPropertyName("new_password")] public string NewPassword { get; init; } = string.Empty;
}

public sealed class SetProfilePinRequest
{
    [JsonPropertyName("pin")] public string Pin { get; init; } = string.Empty;
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
