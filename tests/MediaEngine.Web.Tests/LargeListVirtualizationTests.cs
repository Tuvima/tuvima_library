namespace MediaEngine.Web.Tests;

public sealed class LargeListVirtualizationTests
{
    [Fact]
    public void ListenSongTable_UsesVirtualizeForLargeTrackLists()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Listen\ListenSongTable.razor"));

        Assert.Contains("<Virtualize Items=\"@DisplayTracks\"", source, StringComparison.Ordinal);
        Assert.Contains("@key=\"track.Id\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ListenTrackDataGrid_EnablesMudBlazorVirtualization()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Listen\ListenTrackDataGrid.razor"));

        Assert.Contains("Virtualize=\"true\"", source, StringComparison.Ordinal);
    }

    private static string GetRepoFilePath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
