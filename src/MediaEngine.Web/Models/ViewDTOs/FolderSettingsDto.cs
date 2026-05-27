using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Represents import folder configuration exchanged with the Engine's
/// <c>GET /settings/folders</c> and <c>PUT /settings/folders</c> endpoints.
/// </summary>
public sealed class FolderSettingsDto
{
    public FolderSettingsDto()
    {
    }

    public FolderSettingsDto(List<string>? WatchDirectories)
    {
        this.WatchDirectories = WatchDirectories ?? [];
    }

    [JsonPropertyName("watch_directories")]
    public List<string> WatchDirectories { get; set; } = [];

    [JsonIgnore]
    public IReadOnlyList<string> EffectiveWatchDirectories =>
        WatchDirectories
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
