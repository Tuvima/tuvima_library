namespace MediaEngine.Web.Tests;

public sealed class ViewLibrarySurfaceTests
{
    [Fact]
    public void ViewSurface_ExposesMixedLocalLibraryWithoutCatalogueActions()
    {
        var source = Read("src/MediaEngine.Web/Components/Pages/ViewPage.razor");

        Assert.Contains("@page \"/view\"", source, StringComparison.Ordinal);
        Assert.Contains("@page \"/view/{LibraryId:guid}\"", source, StringComparison.Ordinal);
        Assert.Contains("<MediaSectionShell", source, StringComparison.Ordinal);
        Assert.Contains("Search names, dates, devices, locations, and tags", source, StringComparison.Ordinal);
        Assert.Contains("role=\"tablist\"", source, StringComparison.Ordinal);
        Assert.Contains("aria-modal=\"true\"", source, StringComparison.Ordinal);
        Assert.Contains("ToggleFavoriteAsync", source, StringComparison.Ordinal);
        Assert.Contains("ToggleHiddenAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("view-tile__actions", source, StringComparison.Ordinal);
        Assert.Contains("IsVideo", source, StringComparison.Ordinal);
        Assert.Contains("IsDocument", source, StringComparison.Ordinal);
        Assert.Contains("IsAudio", source, StringComparison.Ordinal);
        Assert.Contains("profileId=", source, StringComparison.Ordinal);
        Assert.Contains("SupplyParameterFromQuery", source, StringComparison.Ordinal);
        Assert.Contains("\"All\"", source, StringComparison.Ordinal);
        Assert.Contains("\"Recent\"", source, StringComparison.Ordinal);
        Assert.Contains("\"Favorites\"", source, StringComparison.Ordinal);
        Assert.Contains("\"Videos\"", source, StringComparison.Ordinal);
        Assert.Contains("\"Documents\"", source, StringComparison.Ordinal);
        Assert.Contains("Add media", source, StringComparison.Ordinal);
        Assert.Contains("<InputFile", source, StringComparison.Ordinal);
        Assert.Contains("UploadViewMediaAsync", source, StringComparison.Ordinal);
        Assert.Contains("role=\"status\"", source, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("@page \"/photos\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Wikidata", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Provider", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MediaEditorLauncherService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ViewStyles_UseResponsiveStableMixedMediaGeometry()
    {
        var styles = Read("src/MediaEngine.Web/Components/Pages/ViewPage.razor.css");

        Assert.Contains("aspect-ratio: 4 / 3;", styles, StringComparison.Ordinal);
        Assert.Contains("object-fit: cover;", styles, StringComparison.Ordinal);
        Assert.Contains(":focus-visible", styles, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 900px)", styles, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 640px)", styles, StringComparison.Ordinal);
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
