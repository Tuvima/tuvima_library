using MediaEngine.Contracts.LocalAssets;

namespace MediaEngine.Web.Services.Editing;

public sealed class GalleryEditorLaunchRequest
{
    public ViewGalleryDto? EditingGallery { get; init; }
    public string InitialMembershipMode { get; init; } = "Manual";
}
