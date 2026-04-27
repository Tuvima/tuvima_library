namespace MediaEngine.Web.Tests;

public sealed class UnifiedDetailComponentTests
{
    [Fact]
    public void HeroBackdrop_DoesNotUseBlurredCoverFallback()
    {
        var source = ReadSource("src/MediaEngine.Web/Components/Details/HeroBackdrop.razor");

        Assert.DoesNotContain("filter: blur(", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ArtworkPresentationMode.CinematicBackdrop", source);
    }

    [Fact]
    public void DetailPage_RendersExpectedSpecializedTabs()
    {
        var source = ReadSource("src/MediaEngine.Web/Components/Details/DetailPage.razor");

        Assert.Contains("EpisodesTab", source);
        Assert.Contains("FormatsTab", source);
        Assert.Contains("SyncTab", source);
        Assert.Contains("RegistryTab", source);
    }

    [Fact]
    public void ExistingMusicAlbumExperience_RemainsOnListenPage()
    {
        var listenPage = ReadSource("src/MediaEngine.Web/Components/Pages/ListenPage.razor");
        var albumRoute = ReadSource("src/MediaEngine.Web/Components/Pages/UnifiedDetailPage.razor");

        Assert.Contains("album", listenPage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@page \"/listen/album", albumRoute, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadSource(string relativePath)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, relativePath));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MediaEngine.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
