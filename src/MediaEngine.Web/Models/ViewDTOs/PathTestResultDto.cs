using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Result returned by <c>POST /settings/test-path</c>.
/// Indicates whether the Engine process can find and access the given folder.
/// </summary>
public sealed record PathTestResultDto(
    [property: JsonPropertyName("path")]      string Path,
    [property: JsonPropertyName("exists")]    bool   Exists,
    [property: JsonPropertyName("has_read")]  bool   HasRead,
    [property: JsonPropertyName("has_write")] bool   HasWrite);
