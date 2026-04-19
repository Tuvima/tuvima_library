using System.Text.Json.Serialization;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Models;

/// <summary>
/// Configurable storage policy for managed assets.
/// </summary>
public sealed class LibraryStoragePolicy
{
    [JsonPropertyName("mode")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public StorageMode Mode { get; set; } = StorageMode.Hybrid;

    [JsonPropertyName("artwork_export")]
    public bool ArtworkExport { get; set; }

    [JsonPropertyName("subtitle_export")]
    public bool SubtitleExport { get; set; } = true;

    [JsonPropertyName("metadata_sidecar_export")]
    public bool MetadataSidecarExport { get; set; }

    [JsonPropertyName("cleanup_managed_local_artwork")]
    public bool CleanupManagedLocalArtwork { get; set; } = true;

    [JsonPropertyName("export_profile")]
    public SidecarExportProfile ExportProfile { get; set; } = new();
}

/// <summary>
/// Compatibility export profile for preferred sidecars.
/// </summary>
public sealed class SidecarExportProfile
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "plex-jellyfin-common";

    [JsonPropertyName("artwork")]
    public bool Artwork { get; set; }

    [JsonPropertyName("preferred_subtitles")]
    public bool PreferredSubtitles { get; set; } = true;

    [JsonPropertyName("metadata_sidecars")]
    public bool MetadataSidecars { get; set; }
}
