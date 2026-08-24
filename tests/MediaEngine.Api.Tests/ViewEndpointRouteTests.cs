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
        Assert.Contains("MapGet(\"/scopes\"", endpoint, StringComparison.Ordinal);
        Assert.Contains("MapGet(\"/assets\"", endpoint, StringComparison.Ordinal);
        Assert.Contains("MapPost(\"/uploads\"", endpoint, StringComparison.Ordinal);
        Assert.Contains("/items/{id:guid}/content", endpoint, StringComparison.Ordinal);
        Assert.Contains("/items/{id:guid}/thumbnail", endpoint, StringComparison.Ordinal);
        Assert.Contains("/galleries", endpoint, StringComparison.Ordinal);
        Assert.Contains("MapGet(\"/share-targets\"", endpoint, StringComparison.Ordinal);
        Assert.Contains("GetGalleryShareTargetsAsync", endpoint, StringComparison.Ordinal);
        Assert.Contains("TryValidateGalleryShares", endpoint, StringComparison.Ordinal);
        Assert.Contains("/admin/libraries/{libraryId:guid}/scan", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("Guid? profileId, HttpContext", endpoint, StringComparison.Ordinal);
        Assert.Contains("Guid? scopeProfileId", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("MapGet(\"/libraries\"", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("MapGet(\"/{libraryId:guid}\"", endpoint, StringComparison.Ordinal);
        Assert.Contains("IViewRequestProfileContext", endpoint, StringComparison.Ordinal);
        Assert.Contains("IViewResourceAuthorizationService", endpoint, StringComparison.Ordinal);
        Assert.Contains(": ViewScopeRequest.Shared;", endpoint, StringComparison.Ordinal);
        Assert.Contains("AuthorizeOwnedItemAsync", endpoint, StringComparison.Ordinal);
        Assert.Contains("if (bitmap is null) return Results.NoContent();", endpoint, StringComparison.Ordinal);
        Assert.Contains("catch { return Results.NoContent(); }", endpoint, StringComparison.Ordinal);
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
