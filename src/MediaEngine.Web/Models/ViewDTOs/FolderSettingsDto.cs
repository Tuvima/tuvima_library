using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Represents the folder configuration exchanged with the Engine's
/// <c>GET /settings/folders</c> and <c>PUT /settings/folders</c> endpoints.
/// </summary>
public sealed class FolderSettingsDto
{
    public FolderSettingsDto()
    {
    }

    public FolderSettingsDto(string WatchDirectory, string LibraryRoot)
    {
        this.WatchDirectory = WatchDirectory;
        this.LibraryRoot = LibraryRoot;
        WatchDirectories = string.IsNullOrWhiteSpace(WatchDirectory) ? [] : [WatchDirectory];
    }

    public FolderSettingsDto(string WatchDirectory, string LibraryRoot, List<string>? WatchDirectories)
    {
        this.WatchDirectory = WatchDirectory;
        this.LibraryRoot = LibraryRoot;
        this.WatchDirectories = WatchDirectories ?? (string.IsNullOrWhiteSpace(WatchDirectory) ? [] : [WatchDirectory]);
    }

    [JsonPropertyName("watch_directory")]
    public string WatchDirectory { get; set; } = string.Empty;

    [JsonPropertyName("watch_directories")]
    public List<string> WatchDirectories { get; set; } = [];

    [JsonPropertyName("library_root")]
    public string LibraryRoot { get; set; } = string.Empty;

    [JsonIgnore]
    public IReadOnlyList<string> EffectiveWatchDirectories =>
        WatchDirectories.Count > 0
            ? WatchDirectories.Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
            : (string.IsNullOrWhiteSpace(WatchDirectory) ? [] : [WatchDirectory]);
}
