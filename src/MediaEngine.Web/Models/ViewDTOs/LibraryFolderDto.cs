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

    [JsonPropertyName("source_path")]
    public string? SourcePath { get; set; }

    [JsonPropertyName("source_paths")]
    public List<string> SourcePaths { get; set; } = [];

    [JsonPropertyName("read_only")]
    public bool ReadOnly { get; set; }

    [JsonPropertyName("writeback_override")]
    public bool? WritebackOverride { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    /// <summary>
    /// All effective source paths (deduped union of source_paths + source_path).
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<string> EffectiveSourcePaths =>
        SourcePaths.Count > 0
            ? SourcePaths
            : (string.IsNullOrWhiteSpace(SourcePath) ? [] : [SourcePath]);
}
