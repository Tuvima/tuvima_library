namespace MediaEngine.Web.Tests;

public sealed class PhotoLibrarySurfaceTests
{
    [Fact]
    public void PhotosSurface_ExposesLocalCurationWithoutCatalogueActions()
    {
        var source = Read("src/MediaEngine.Web/Components/Pages/PhotosPage.razor");

        Assert.Contains("@page \"/photos\"", source, StringComparison.Ordinal);
        Assert.Contains("Your private, local timeline. Originals stay in their folders.", source, StringComparison.Ordinal);
        Assert.Contains("role=\"tablist\"", source, StringComparison.Ordinal);
        Assert.Contains("aria-modal=\"true\"", source, StringComparison.Ordinal);
        Assert.Contains("ToggleFavoriteAsync", source, StringComparison.Ordinal);
        Assert.Contains("ToggleHiddenAsync", source, StringComparison.Ordinal);
        Assert.Contains("AddSelectionToAlbumAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Wikidata", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Provider", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MediaEditorLauncherService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PhotosStyles_UseSharedTypographyAndStableSquareGeometry()
    {
        var styles = Read("src/MediaEngine.Web/Components/Pages/PhotosPage.razor.css");

        Assert.Contains("var(--tl-font-display", styles, StringComparison.Ordinal);
        Assert.Contains("var(--tl-font-ui", styles, StringComparison.Ordinal);
        Assert.Contains("aspect-ratio: 1;", styles, StringComparison.Ordinal);
        Assert.Contains("object-fit: cover;", styles, StringComparison.Ordinal);
        Assert.Contains(":focus-visible", styles, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 900px)", styles, StringComparison.Ordinal);
    }

    private static string Read(string relativePath)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MediaEngine.slnx")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }
}
