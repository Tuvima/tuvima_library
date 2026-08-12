using System.Text.Json.Serialization;
using System.Text.Json;
using MediaEngine.Contracts.Collections;
using MediaEngine.Domain.Services;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Dashboard presentation behavior layered over the shared managed-collection wire contract.
/// </summary>
public sealed class ManagedCollectionViewModel : ManagedCollectionDto
{
    public static ManagedCollectionViewModel FromContract(ManagedCollectionDto source) =>
        JsonSerializer.Deserialize<ManagedCollectionViewModel>(
            JsonSerializer.Serialize(source, MediaEngineJson.Web),
            MediaEngineJson.Web)
        ?? throw new InvalidOperationException("Could not map managed collection for presentation.");

    [JsonIgnore]
    public bool IsShared =>
        string.Equals(Visibility, "shared", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public string TypeColor => CollectionType switch
    {
        "Smart" => "var(--tl-status-info)",
        "System" => "var(--tl-status-success)",
        "Mix" => "#A78BFA",
        "Playlist" => "var(--tl-accent-primary)",
        _ => "var(--tl-status-info)",
    };

    [JsonIgnore]
    public string TypeLabel => CollectionType switch
    {
        "Smart" => "Smart",
        "System" => "System",
        "Mix" => "Mix",
        "Playlist" => "Playlist",
        "PlaylistFolder" => "Folder",
        _ => CollectionType,
    };

    [JsonIgnore]
    public string StatusColor => Status switch
    {
        "Active" => "var(--tl-status-success)",
        "Disabled" => "rgba(255,255,255,0.4)",
        "Empty" => "var(--tl-status-warning)",
        _ => "var(--tl-status-success)",
    };

    [JsonIgnore]
    public string StatusLabel => Status switch
    {
        "Active" => "Active",
        "Disabled" => "Disabled",
        "Empty" => "Empty",
        _ => Status,
    };
}

/// <summary>
/// Collections-hub display behavior layered over the shared catalog wire contract.
/// </summary>
public sealed class CollectionManagementCatalogViewModel : CollectionManagementCatalogDto
{
    public static CollectionManagementCatalogViewModel FromContract(CollectionManagementCatalogDto source) =>
        JsonSerializer.Deserialize<CollectionManagementCatalogViewModel>(
            JsonSerializer.Serialize(source, MediaEngineJson.Web),
            MediaEngineJson.Web)
        ?? throw new InvalidOperationException("Could not map collection catalog entry for presentation.");

    [JsonIgnore]
    public string ArtworkUrl => CoverArtworkUrl ?? string.Empty;

    [JsonIgnore]
    public string TypeLabel => CollectionType switch
    {
        "System" => "System",
        "Playlist" => "Playlist",
        "Smart" => "Smart",
        "Mix" => "Mix",
        "Custom" => "Curated Collection",
        "Universe" or "Series" or "ContentGroup" => "Generated Collection",
        _ => IsManual ? "Custom Collection" : "Generated Collection",
    };

    [JsonIgnore]
    public string FamilyLabel => Family switch
    {
        "Global" => "Global",
        "System" => "System",
        "Discover" => "Discover",
        _ => "Curated",
    };

    [JsonIgnore]
    public bool IsManual =>
        string.Equals(Resolution, "materialized", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public string StatusLabel => !IsEnabled ? "Disabled" : ItemCount == 0 ? "Empty" : "Active";
}
