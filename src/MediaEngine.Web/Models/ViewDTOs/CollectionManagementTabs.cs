namespace MediaEngine.Web.Models.ViewDTOs;

public sealed record CollectionManagementTab(
    string Id,
    string Label,
    string Icon,
    int Count);

public static class CollectionManagementTabs
{
    public static IReadOnlyList<CollectionManagementTab> Build(
        IReadOnlyCollection<CollectionListItemViewModel> collections,
        Guid? activeProfileId)
    {
        var all = collections.Count;
        var mine = collections.Count(collection =>
            !collection.IsShared
            && !collection.IsPlaylist
            && collection.ProfileId == activeProfileId);
        var shared = collections.Count(collection => collection.IsShared && !collection.IsPlaylist);
        var playlists = collections.Count(collection => collection.IsPlaylist);

        return
        [
            new("all", "All", MudBlazor.Icons.Material.Outlined.Dashboard, all),
            new("mine", "My Collections", MudBlazor.Icons.Material.Outlined.Person, mine),
            new("shared", "Shared", MudBlazor.Icons.Material.Outlined.Groups, shared),
            new("playlists", "Playlists", MudBlazor.Icons.Material.Outlined.QueueMusic, playlists),
        ];
    }

    public static IEnumerable<CollectionListItemViewModel> Filter(
        IEnumerable<CollectionListItemViewModel> collections,
        string tabId,
        Guid? activeProfileId) =>
        tabId switch
        {
            "mine" => collections.Where(collection =>
                !collection.IsShared
                && !collection.IsPlaylist
                && collection.ProfileId == activeProfileId),
            "shared" => collections.Where(collection => collection.IsShared && !collection.IsPlaylist),
            "playlists" => collections.Where(collection => collection.IsPlaylist),
            _ => collections,
        };
}
