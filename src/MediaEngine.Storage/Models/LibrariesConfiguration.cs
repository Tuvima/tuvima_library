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

    /// <summary>Absolute path of the folder monitored or imported by the Engine.</summary>
    [JsonPropertyName("source_path")]
    public string SourcePath { get; set; } = string.Empty;

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
}
