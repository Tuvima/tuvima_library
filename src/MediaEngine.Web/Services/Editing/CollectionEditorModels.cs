using MediaEngine.Web.Models.ViewDTOs;

namespace MediaEngine.Web.Services.Editing;

public sealed class CollectionEditorLaunchRequest
{
    public CollectionListItemViewModel? EditingCollection { get; init; }
    public Guid? ActiveProfileId { get; init; }
    public bool CanManageSharedCollections { get; init; }
    public string Mode { get; init; } = "Collection";
    public string? InitialCollectionType { get; init; }
    public bool? InitialRulesEnabled { get; init; }
    public string? InitialTitle { get; init; }
}
