using MediaEngine.Web.Models.ViewDTOs;

namespace MediaEngine.Web.Services.Editing;

public sealed class CollectionEditorLaunchRequest
{
    public CollectionListItemViewModel? EditingCollection { get; init; }
    public Guid? ActiveProfileId { get; init; }
    public bool CanManageSharedCollections { get; init; }
    public CollectionEditorMode Mode { get; init; } = CollectionEditorMode.CuratedCollection;
    public string? InitialCollectionType { get; init; }
    public bool? InitialRulesEnabled { get; init; }
    public string? InitialTitle { get; init; }
    public Guid? TriggeringWorkId { get; init; }
    public string? TriggeringWorkTitle { get; init; }
    public string? TriggeringMediaType { get; init; }
    public string? TriggeringYear { get; init; }
    public string? TriggeringArtworkUrl { get; init; }
}

public enum CollectionEditorMode
{
    CuratedCollection,
    ManualPlaylist,
    SmartPlaylist,
}
