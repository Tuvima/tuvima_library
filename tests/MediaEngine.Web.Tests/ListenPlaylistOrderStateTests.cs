using System.Text.Json.Nodes;
using MediaEngine.Web.Services.Navigation;

namespace MediaEngine.Web.Tests;

public sealed class ListenPlaylistOrderStateTests
{
    [Fact]
    public void Write_PreservesUnrelatedNavigationSettings()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var json = ListenPlaylistOrderState.Write("{\"compactNavigation\":true}", [first, second]);
        var root = JsonNode.Parse(json)!.AsObject();

        Assert.True(root["compactNavigation"]!.GetValue<bool>());
        Assert.Equal([first, second], ListenPlaylistOrderState.Read(json));
    }

    [Fact]
    public void Read_IgnoresInvalidAndDuplicateIds()
    {
        var id = Guid.NewGuid();
        var json = $"{{\"listenPlaylistOrder\":[\"bad\",\"{id:D}\",\"{id:D}\"]}}";

        Assert.Equal([id], ListenPlaylistOrderState.Read(json));
    }

    [Fact]
    public void TryMoveByOffset_MovesWithinBounds()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        var moved = ListenPlaylistOrderState.TryMoveByOffset(ids, ids[1], -1, out var updated);

        Assert.True(moved);
        Assert.Equal([ids[1], ids[0], ids[2]], updated);
    }

    [Fact]
    public void TryMoveBefore_UsesDropTargetPosition()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        var moved = ListenPlaylistOrderState.TryMoveBefore(ids, ids[2], ids[0], out var updated);

        Assert.True(moved);
        Assert.Equal([ids[2], ids[0], ids[1]], updated);
    }
}
