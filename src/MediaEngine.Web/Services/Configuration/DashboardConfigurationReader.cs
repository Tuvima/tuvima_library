using System.Text.Json;
using System.Text.Json.Serialization;
using MediaEngine.Domain.Models;

namespace MediaEngine.Web.Services.Configuration;

public sealed class DashboardConfigurationReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string _configDirectory;

    public DashboardConfigurationReader(string configDirectory)
    {
        _configDirectory = string.IsNullOrWhiteSpace(configDirectory) ? "config" : configDirectory;
    }

    public DashboardCoreConfiguration LoadCore() =>
        LoadJson<DashboardCoreConfiguration>("core.json") ?? new();

    public PaletteConfiguration LoadPalette() =>
        LoadJson<PaletteConfiguration>(Path.Combine("ui", "palette.json")) ?? new();

    private T? LoadJson<T>(string relativePath)
    {
        var path = Path.Combine(_configDirectory, relativePath);
        if (!File.Exists(path))
            return default;

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, JsonOptions);
    }
}

public sealed class DashboardCoreConfiguration
{
    [JsonPropertyName("auth")]
    public DashboardAuthSettings Auth { get; set; } = new();
}

public sealed class DashboardAuthSettings
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "Local";

    [JsonPropertyName("oidc")]
    public DashboardOidcSettings Oidc { get; set; } = new();
}

public sealed class DashboardOidcSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("authority")]
    public string Authority { get; set; } = string.Empty;

    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = string.Empty;

    [JsonPropertyName("client_secret")]
    public string? ClientSecret { get; set; }

    [JsonPropertyName("scopes")]
    public List<string> Scopes { get; set; } = ["openid", "profile", "email"];
}
