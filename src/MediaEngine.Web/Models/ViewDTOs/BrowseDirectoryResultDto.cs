using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Result returned by <c>POST /settings/browse-directory</c>.
/// Lists subdirectories at a given path, or drive roots when the path is empty.
/// </summary>
public sealed record BrowseDirectoryResultDto(
    [property: JsonPropertyName("current_path")]  string         CurrentPath,
    [property: JsonPropertyName("parent_path")]   string?        ParentPath,
    [property: JsonPropertyName("directories")]   List<string>   Directories);
