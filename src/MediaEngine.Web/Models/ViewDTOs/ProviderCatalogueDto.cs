using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Dashboard-side DTO for a provider catalogue entry.
/// Deserialized from <c>GET /providers/catalogue</c>.
/// </summary>
public sealed class ProviderCatalogueDto
{
    [JsonPropertyName("providerId")]
    public string ProviderId { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("domain")]
    public string Domain { get; set; } = "";

    [JsonPropertyName("mediaTypes")]
    public List<string> MediaTypes { get; set; } = [];

    [JsonPropertyName("accentColor")]
    public string AccentColor { get; set; } = "#90A4AE";

    [JsonPropertyName("materialIcon")]
    public string MaterialIcon { get; set; } = "Cloud";

    [JsonPropertyName("externalUrlTemplate")]
    public string? ExternalUrlTemplate { get; set; }

    [JsonPropertyName("category")]
    public string Category { get; set; } = "Open";

    [JsonPropertyName("requiresKey")]
    public bool RequiresKey { get; set; }

    [JsonPropertyName("authType")]
    public string AuthType { get; set; } = "none";

    [JsonPropertyName("searchChips")]
    public Dictionary<string, List<string>> SearchChips { get; set; } = [];

    [JsonPropertyName("rankingChips")]
    public Dictionary<string, List<string>> RankingChips { get; set; } = [];

    [JsonPropertyName("iconPath")]
    public string? IconPath { get; set; }

    [JsonPropertyName("hydrationStages")]
    public List<int> HydrationStages { get; set; } = [];

    [JsonPropertyName("languageStrategy")]
    public string LanguageStrategy { get; set; } = "source";
}
