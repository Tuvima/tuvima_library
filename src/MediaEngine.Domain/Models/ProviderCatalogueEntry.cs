namespace MediaEngine.Domain.Models;

/// <summary>
/// Provider catalogue entry with UI metadata, served via GET /providers/catalogue.
/// Centralises display names, accent colors, icons, and field capabilities
/// that were previously hardcoded across Dashboard files.
/// </summary>
public sealed class ProviderCatalogueEntry
{
    public string ProviderId { get; init; } = "";
    public string Name { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public bool Enabled { get; init; }
    public string Domain { get; init; } = "";
    public IReadOnlyList<string> MediaTypes { get; init; } = [];
    public string AccentColor { get; init; } = "#90A4AE";
    public string MaterialIcon { get; init; } = "Cloud";
    public string? ExternalUrlTemplate { get; init; }
    public string Category { get; init; } = "Open";
    public bool RequiresKey { get; init; }
    public string AuthType { get; init; } = "none";
    public IReadOnlyDictionary<string, IReadOnlyList<string>> SearchChips { get; init; } = new Dictionary<string, IReadOnlyList<string>>();
    public IReadOnlyDictionary<string, IReadOnlyList<string>> RankingChips { get; init; } = new Dictionary<string, IReadOnlyList<string>>();
    public string? IconPath { get; init; }
    public int[] HydrationStages { get; init; } = [];
    public string LanguageStrategy { get; init; } = "source";
}
