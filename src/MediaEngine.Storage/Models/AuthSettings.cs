using System.Text.Json.Serialization;

namespace MediaEngine.Storage.Models;

public sealed class AuthSettings
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "DisabledLocalOnly";

    [JsonPropertyName("localhost_bypass")]
    public bool LocalhostBypass { get; set; } = true;

    [JsonPropertyName("require_https_remote")]
    public bool RequireHttpsRemote { get; set; }

    [JsonPropertyName("oidc")]
    public OidcSettings Oidc { get; set; } = new();
}

public sealed class OidcSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("authority")]
    public string Authority { get; set; } = string.Empty;

    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = string.Empty;

    [JsonPropertyName("client_secret")]
    public string ClientSecret { get; set; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = "OpenID Connect";

    [JsonPropertyName("scopes")]
    public List<string> Scopes { get; set; } = ["openid", "profile", "email"];
}
