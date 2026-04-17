using MediaEngine.Web.Models.ViewDTOs;

namespace MediaEngine.Web.Tests;

public sealed class CollectionManagementTabsTests
{
    private static readonly Guid ActiveProfileId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherProfileId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void Build_SeparatesPrivateSharedAndPlaylistCounts()
    {
        var collections = new[]
        {
            CreateCollection("My Smart", "Custom", "private", ActiveProfileId),
            CreateCollection("Shared Picks", "Custom", "shared", null),
            CreateCollection("Road Trip", "Playlist", "private", ActiveProfileId),
            CreateCollection("Another User", "Custom", "private", OtherProfileId),
        };

        var tabs = CollectionManagementTabs.Build(collections, ActiveProfileId).ToDictionary(tab => tab.Id);

        Assert.Equal(4, tabs["all"].Count);
        Assert.Equal(1, tabs["mine"].Count);
        Assert.Equal(1, tabs["shared"].Count);
        Assert.Equal(1, tabs["playlists"].Count);
    }

    [Fact]
    public void Filter_MineOnly_ReturnsPrivateCollectionsOwnedByActiveProfile()
    {
        var collections = new[]
        {
            CreateCollection("Mine", "Custom", "private", ActiveProfileId),
            CreateCollection("Shared", "Custom", "shared", null),
            CreateCollection("Playlist", "Playlist", "private", ActiveProfileId),
            CreateCollection("Other", "Custom", "private", OtherProfileId),
        };

        var result = CollectionManagementTabs.Filter(collections, "mine", ActiveProfileId).ToList();

        Assert.Single(result);
        Assert.Equal("Mine", result[0].Name);
    }

    [Fact]
    public void Filter_Playlists_ReturnsBothPrivateAndSharedPlaylists()
    {
        var collections = new[]
        {
            CreateCollection("Private Playlist", "Playlist", "private", ActiveProfileId),
            CreateCollection("Shared Playlist", "Playlist", "shared", null),
            CreateCollection("Shared Collection", "Custom", "shared", null),
        };

        var result = CollectionManagementTabs.Filter(collections, "playlists", ActiveProfileId).ToList();

        Assert.Equal(2, result.Count);
        Assert.All(result, item => Assert.Equal("Playlist", item.CollectionType));
    }

    private static CollectionListItemViewModel CreateCollection(
        string name,
        string type,
        string visibility,
        Guid? profileId) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            CollectionType = type,
            Visibility = visibility,
            ProfileId = profileId,
        };
}
