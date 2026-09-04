using System.Text.Json.Serialization;

namespace MediaEngine.Domain.Configuration;

public sealed class AuthSettings
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "Local";

    [JsonPropertyName("localhost_bypass")]
    public bool LocalhostBypass { get; set; }

    [JsonPropertyName("require_https_remote")]
    public bool RequireHttpsRemote { get; set; }

    [JsonPropertyName("external_providers")]
    public List<ExternalAuthProviderSettings> ExternalProviders { get; set; } = [];
}

public static class ExternalAuthProviderKinds
{
    public const string OpenIdConnect = "oidc";
    public const string OAuth = "oauth";
}

public sealed class ExternalAuthProviderSettings
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = ExternalAuthProviderKinds.OpenIdConnect;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = "OpenID Connect";

    [JsonPropertyName("issuer")]
    public string Issuer { get; set; } = string.Empty;

    [JsonPropertyName("authority")]
    public string Authority { get; set; } = string.Empty;

    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = string.Empty;

    [JsonIgnore]
    public string ClientSecret { get; set; } = string.Empty;

    [JsonPropertyName("scopes")]
    public List<string> Scopes { get; set; } = ["openid", "profile", "email"];

    [JsonPropertyName("use_pkce")]
    public bool UsePkce { get; set; } = true;

    [JsonPropertyName("authorization_endpoint")]
    public string AuthorizationEndpoint { get; set; } = string.Empty;

    [JsonPropertyName("token_endpoint")]
    public string TokenEndpoint { get; set; } = string.Empty;

    [JsonPropertyName("user_information_endpoint")]
    public string UserInformationEndpoint { get; set; } = string.Empty;

    [JsonPropertyName("id_claim")]
    public string IdClaim { get; set; } = "id";

    [JsonPropertyName("name_claim")]
    public string NameClaim { get; set; } = "name";

    [JsonPropertyName("email_claim")]
    public string EmailClaim { get; set; } = "email";
}
