namespace MediaEngine.Api.Tests;

public sealed class ViewDiscoveryEndpointRouteTests
{
    [Fact]
    public void DiscoveryRoutesAreCleanAndDoNotAcceptPhysicalLibraryIds()
    {
        var root = FindRepositoryRoot();
        var endpoints = File.ReadAllText(Path.Combine(
            root, "src", "MediaEngine.Api", "Endpoints", "ViewDiscoveryEndpoints.cs"));
        var mapper = File.ReadAllText(Path.Combine(
            root, "src", "MediaEngine.Api", "DependencyInjection", "ApiEndpointRouteBuilderExtensions.cs"));

        Assert.Contains("group.MapGet(\"/places\"", endpoints, StringComparison.Ordinal);
        Assert.Contains("group.MapGet(\"/people\"", endpoints, StringComparison.Ordinal);
        Assert.DoesNotContain("libraryId", endpoints, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("libraryIds", endpoints, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("app.MapViewDiscoveryEndpoints();", mapper, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MediaEngine.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
