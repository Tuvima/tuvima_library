using MediaEngine.Contracts.Artwork;
using MudBlazor;

namespace MediaEngine.Web.Components.Artwork;

public sealed record ArtworkRolePresentation(
    string Role,
    string? SourceAssetType,
    string Label,
    string Icon,
    bool IsDefault,
    bool SupportsAutomatic);

public static class ArtworkRolePresentationResolver
{
    public static IReadOnlyList<ArtworkRolePresentation> Resolve(
        ArtworkEntityWorkspaceDto? workspace,
        ArtworkLibraryItemDto item)
    {
        var descriptors = workspace?.SupportedRoles.Count > 0
            ? workspace.SupportedRoles
            : ArtworkRoleCatalog.Resolve(item.EntityType, item.MediaType, item.GroupKind, item.AssetTypes);
        return descriptors.Select(Resolve).ToList();
    }

    public static ArtworkRolePresentation Resolve(ArtworkRoleDescriptorDto descriptor)
    {
        var (label, icon) = descriptor.PresentationKey switch
        {
            "poster-cover" => ("Poster / Cover", Icons.Material.Outlined.Image),
            "poster" => ("Poster", Icons.Material.Outlined.Image),
            "cover" => ("Cover", Icons.Material.Outlined.MenuBook),
            "still" => ("Still", Icons.Material.Outlined.Photo),
            "portrait" => ("Portrait", Icons.Material.Outlined.Portrait),
            "background" => ("Background", Icons.Material.Outlined.Panorama),
            "logo" => ("Logo", Icons.Material.Outlined.BrandingWatermark),
            "primary-artwork" => ("Primary artwork", Icons.Material.Outlined.Image),
            _ => (descriptor.Role, Icons.Material.Outlined.Image),
        };
        return new(descriptor.Role, descriptor.SourceAssetType, label, icon, descriptor.IsDefault, descriptor.SupportsAutomatic);
    }
}
