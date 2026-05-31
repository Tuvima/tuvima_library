using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Dashboard view of a single configured library folder from
/// <c>config/libraries.json</c>. Matches the anonymous object
/// returned by <c>GET /settings/libraries</c>.
/// Spec: side-by-side-with-Plex plan §I.
/// </summary>
public sealed class LibraryFolderDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("media_types")]
    public List<string> MediaTypes { get; set; } = [];

    [JsonPropertyName("source_paths")]
    public List<string> SourcePaths { get; set; } = [];

    [JsonPropertyName("library_root")]
    public string? LibraryRoot { get; set; }

    [JsonPropertyName("intake_mode")]
    public string IntakeMode { get; set; } = "watch";

    [JsonPropertyName("include_subdirectories")]
    public bool IncludeSubdirectories { get; set; } = true;

    [JsonPropertyName("read_only")]
    public bool ReadOnly { get; set; }

    [JsonPropertyName("writeback_override")]
    public bool? WritebackOverride { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    /// <summary>
    /// All configured source paths.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<string> EffectiveSourcePaths =>
        SourcePaths;
}
