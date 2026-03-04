using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Dashboard view model for a file sitting in the Watch Folder.
/// Maps from the Engine's <c>GET /ingestion/watch-folder</c> response.
/// </summary>
public sealed record WatchFolderFileViewModel(
    [property: JsonPropertyName("file_name")]       string FileName,
    [property: JsonPropertyName("relative_path")]   string RelativePath,
    [property: JsonPropertyName("file_size_bytes")] long   FileSizeBytes,
    [property: JsonPropertyName("last_modified")]   DateTimeOffset LastModified)
{
    /// <summary>Human-friendly file size (e.g. "4.2 MB").</summary>
    [JsonIgnore]
    public string FormattedSize => FileSizeBytes switch
    {
        < 1024                       => $"{FileSizeBytes} B",
        < 1024 * 1024                => $"{FileSizeBytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024        => $"{FileSizeBytes / (1024.0 * 1024):F1} MB",
        _                            => $"{FileSizeBytes / (1024.0 * 1024 * 1024):F2} GB",
    };
}

/// <summary>
/// Wrapper for the Watch Folder listing response.
/// </summary>
public sealed record WatchFolderResponse(
    [property: JsonPropertyName("watch_directory")] string? WatchDirectory,
    [property: JsonPropertyName("files")]           List<WatchFolderFileViewModel> Files);
