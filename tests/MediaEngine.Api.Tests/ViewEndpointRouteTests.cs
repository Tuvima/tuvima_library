namespace MediaEngine.Api.Tests;

public sealed class ViewEndpointRouteTests
{
    [Fact]
    public void EngineMapsOnlyTheViewLocalAssetSurface()
    {
        var root = FindRepoRoot();
        var routes = File.ReadAllText(Path.Combine(
            root, "src", "MediaEngine.Api", "DependencyInjection", "ApiEndpointRouteBuilderExtensions.cs"));
        var hosted = File.ReadAllText(Path.Combine(
            root, "src", "MediaEngine.Api", "DependencyInjection", "TuvimaHostedServiceCollectionExtensions.cs"));
        var endpointPath = Path.Combine(
            root, "src", "MediaEngine.Api", "Endpoints", "ViewEndpoints.cs");
        var endpoint = File.ReadAllText(endpointPath);

        Assert.Contains("app.MapViewEndpoints();", routes, StringComparison.Ordinal);
        Assert.DoesNotContain("MapPhotoEndpoints", routes, StringComparison.Ordinal);
        Assert.DoesNotContain("PhotoLibraryIndexHostedService", hosted, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(
            root, "src", "MediaEngine.Api", "Endpoints", "PhotoEndpoints.cs")));
        Assert.Contains("MapGroup(\"/view\")", endpoint, StringComparison.Ordinal);
        Assert.Contains("MapGet(\"/libraries\"", endpoint, StringComparison.Ordinal);
        Assert.Contains("MapGet(\"/{libraryId:guid}\"", endpoint, StringComparison.Ordinal);
        Assert.Contains("MapPost(\"/{libraryId:guid}/scan\"", endpoint, StringComparison.Ordinal);
        Assert.Contains("/{libraryId:guid}/items/{id:guid}/content", endpoint, StringComparison.Ordinal);
        Assert.Contains("/{libraryId:guid}/items/{id:guid}/thumbnail", endpoint, StringComparison.Ordinal);
        Assert.Contains("Guid? profileId", endpoint, StringComparison.Ordinal);
        Assert.Contains("GetRole(httpContext)", endpoint, StringComparison.Ordinal);
        Assert.Contains("LibraryAccessAction.Read", endpoint, StringComparison.Ordinal);
        Assert.Contains("LibraryAccessAction.Contribute", endpoint, StringComparison.Ordinal);
        Assert.Contains("LibraryAccessAction.Manage", endpoint, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MediaEngine.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
