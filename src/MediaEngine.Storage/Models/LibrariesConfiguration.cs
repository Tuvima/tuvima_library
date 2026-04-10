using System.Text.Json.Serialization;

namespace MediaEngine.Storage.Models;

/// <summary>
/// Root model for <c>config/libraries.json</c>.
/// Contains one entry per configured library folder.
/// </summary>
public sealed class LibrariesConfiguration
{
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; set; } = "1.0";

    [JsonPropertyName("libraries")]
    public List<LibraryFolderConfig> Libraries { get; set; } = [];
}

/// <summary>
/// A single library folder entry from <c>config/libraries.json</c>.
/// Defines the source path, category, configured media types, and intake behaviour.
/// </summary>
public sealed class LibraryFolderConfig
{
    /// <summary>Content category (e.g. "Books", "Movies", "TV").</summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Configured media types for this folder (e.g. ["Epub", "Audiobook"]).
    /// These string values map to <see cref="MediaEngine.Domain.Enums.MediaType"/> enum members.
    /// </summary>
    [JsonPropertyName("media_types")]
    public List<string> MediaTypes { get; set; } = [];

    /// <summary>
    /// Absolute path of the folder monitored or imported by the Engine.
    /// Legacy single-path form. New installs should prefer <see cref="SourcePaths"/>.
    /// When both are present, <see cref="SourcePaths"/> wins and this field is ignored.
    /// </summary>
    [JsonPropertyName("source_path")]
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// Absolute paths of all folders that belong to this logical library.
    /// Multi-path libraries let a single library span several drives
    /// (e.g. <c>D:\Movies</c> and <c>E:\Movies</c> as one Movies library),
    /// the same way Plex and Jellyfin allow. When null or empty, the loader
    /// falls back to <see cref="SourcePath"/> for backward compatibility.
    /// Spec: side-by-side-with-Plex plan §F.
    /// </summary>
    [JsonPropertyName("source_paths")]
    public List<string>? SourcePaths { get; set; }

    /// <summary>Absolute path of the organised library destination.</summary>
    [JsonPropertyName("library_root")]
    public string LibraryRoot { get; set; } = string.Empty;

    /// <summary>Intake mode: "watch" (move files in) or "import" (copy or move existing collection).</summary>
    [JsonPropertyName("intake_mode")]
    public string IntakeMode { get; set; } = "watch";

    /// <summary>Whether to monitor subdirectories of the source path. Default: true.</summary>
    [JsonPropertyName("include_subdirectories")]
    public bool IncludeSubdirectories { get; set; } = true;

    /// <summary>Optional free-text notes for the user. Not used by the Engine.</summary>
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    /// <summary>
    /// Hard read-only gate. When true, Tuvima will not move, rename, or write
    /// file tags for any file in this library — it indexes everything in place.
    /// This is the escape hatch for users who want a strictly hands-off mirror
    /// of an external library (e.g. a Plex library Tuvima should not touch).
    /// Default: false. Spec: side-by-side-with-Plex plan §I.
    /// </summary>
    [JsonPropertyName("read_only")]
    public bool ReadOnly { get; set; }

    /// <summary>
    /// Per-library override for metadata writeback. <c>null</c> means use the
    /// global <c>metadata_writeback.enabled</c> flag; <c>true</c> or <c>false</c>
    /// forces on/off for this library only. The user's way of saying
    /// "Plex is my primary, don't touch tags on this one" without turning off
    /// writeback globally. Spec: side-by-side-with-Plex plan §I.
    /// </summary>
    [JsonPropertyName("writeback_override")]
    public bool? WritebackOverride { get; set; }
}
