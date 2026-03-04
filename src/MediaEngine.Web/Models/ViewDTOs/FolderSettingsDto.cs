using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Represents the folder configuration exchanged with the Engine's
/// <c>GET /settings/folders</c> and <c>PUT /settings/folders</c> endpoints.
/// </summary>
public sealed record FolderSettingsDto(
    [property: JsonPropertyName("watch_directory")]   string WatchDirectory,
    [property: JsonPropertyName("library_root")]      string LibraryRoot,
    [property: JsonPropertyName("staging_directory")] string StagingDirectory = "");
