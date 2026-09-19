namespace MediaEngine.Api.Tests;

public sealed class SharedEntityEditorEndpointRouteTests
{
    [Fact]
    public void SharedEditorRoutes_DeclareGraphTargetsPermissionsAndMultipartUploads()
    {
        var source = File.ReadAllText(RepoFile(@"src\MediaEngine.Api\Endpoints\SharedEntityEditorEndpoints.cs"));
        Assert.Contains("/entity-editor", source, StringComparison.Ordinal);
        Assert.Contains("/universes/{qid}/entities/{id:guid}/context", source, StringComparison.Ordinal);
        Assert.Contains("/universes/{qid}/artwork/{assetType}/upload", source, StringComparison.Ordinal);
        Assert.Contains("/universes/{qid}/entities/{id:guid}/artwork/{assetType}/upload", source, StringComparison.Ordinal);
        Assert.Contains("Accepts<IFormFile>(\"multipart/form-data\")", source, StringComparison.Ordinal);
        Assert.Contains("RequireAdministratorOrApplication(ApplicationPermissionIds.MetadataWrite)", source, StringComparison.Ordinal);
        Assert.Contains("trigger_type\"] = \"universe_sweep\"", source, StringComparison.Ordinal);
        Assert.Contains("FindWorkIdsByProvenanceQidAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new(\"files\"", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SharedEditorContracts_HaveStableGraphSectionsAndBreadcrumb()
    {
        var source = File.ReadAllText(RepoFile(@"src\MediaEngine.Contracts\Universe\SharedEntityEditorContracts.cs"));
        Assert.Contains("public const string Universe = \"Universe\"", source, StringComparison.Ordinal);
        Assert.Contains("public const string FictionalEntity = \"FictionalEntity\"", source, StringComparison.Ordinal);
        Assert.Contains("public const string Artwork = \"artwork\"", source, StringComparison.Ordinal);
        Assert.Contains("breadcrumb", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Files =", source, StringComparison.Ordinal);
    }

    private static string RepoFile(string relative, [System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourceFile)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MediaEngine.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, relative);
    }
}
