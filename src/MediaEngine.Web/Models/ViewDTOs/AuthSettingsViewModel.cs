using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

public sealed record AuthSettingsViewModel(
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("localhost_bypass")] bool LocalhostBypass,
    [property: JsonPropertyName("require_https_remote")] bool RequireHttpsRemote,
    [property: JsonPropertyName("oidc_enabled")] bool OidcEnabled,
    [property: JsonPropertyName("oidc_display_name")] string OidcDisplayName,
    [property: JsonPropertyName("oidc_authority")] string OidcAuthority,
    [property: JsonPropertyName("oidc_client_id")] string OidcClientId,
    [property: JsonPropertyName("oidc_scopes")] List<string> OidcScopes);
