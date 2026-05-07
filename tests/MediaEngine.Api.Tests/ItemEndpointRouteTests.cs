using System.IO;

namespace MediaEngine.Api.Tests;

public sealed class ItemEndpointRouteTests
{
    [Fact]
    public void LibraryItemEndpoints_UseLibraryItemsRootWithoutDuplicateItemsSegment()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Endpoints\LibraryItemEndpoints.cs"));

        Assert.Contains("var group = app.MapGroup(\"/library/items\")", source, StringComparison.Ordinal);
        Assert.Contains("group.MapGet(\"\", async (", source, StringComparison.Ordinal);
        Assert.Contains("group.MapGet(\"/{entityId}/detail\", async (", source, StringComparison.Ordinal);
        Assert.Contains("group.MapGet(\"/{entityId:guid}/history\", async (", source, StringComparison.Ordinal);
        Assert.DoesNotContain("group.MapGet(\"/items\", async (", source, StringComparison.Ordinal);
        Assert.DoesNotContain("group.MapGet(\"/items/{entityId}/detail\", async (", source, StringComparison.Ordinal);
        Assert.DoesNotContain("group.MapGet(\"/items/{entityId:guid}/history\", async (", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Program_MapsLibraryItemEndpointsOnly()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\DependencyInjection\ApiEndpointRouteBuilderExtensions.cs"));
        var oldSurface = "Reg" + "istry";

        Assert.Contains("MapLibraryItemEndpoints", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Map" + oldSurface + "Endpoints", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TrackedSource_DoesNotContainRemovedLibraryItemSurfaceNames()
    {
        var root = GetRepoFilePath("");
        var removedTerm = "reg" + "istry";
        var removedRoute = "/" + removedTerm;
        var ignoredSegments = new[]
        {
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}.data{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}logs{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}.claude{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}.tmp{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}wwwroot{Path.DirectorySeparatorChar}lib{Path.DirectorySeparatorChar}",
        };
        var textExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".razor", ".css", ".js", ".json", ".md", ".xml", ".iss", ".ps1", ".bat", ".sql", ".yml", ".yaml", ".txt"
        };

        var offenders = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => ignoredSegments.All(segment => !path.Contains(segment, StringComparison.OrdinalIgnoreCase)))
            .Where(path => textExtensions.Contains(Path.GetExtension(path)))
            .Where(path => !Path.GetFileName(path).Equals("api.log", StringComparison.OrdinalIgnoreCase))
            .Where(path =>
            {
                var relative = Path.GetRelativePath(root, path);
                return !relative.StartsWith(@"src\MediaEngine.Contracts\Details\", StringComparison.OrdinalIgnoreCase)
                    && !relative.StartsWith(@"src\MediaEngine.Api\Services\Details\", StringComparison.OrdinalIgnoreCase)
                    && !relative.StartsWith(@"src\MediaEngine.Web\Components\Details\", StringComparison.OrdinalIgnoreCase)
                    && !relative.Equals(@"tests\MediaEngine.Web.Tests\UnifiedDetailComponentTests.cs", StringComparison.OrdinalIgnoreCase);
            })
            .Where(path =>
            {
                var text = File.ReadAllText(path);
                return text.Contains(removedTerm, StringComparison.OrdinalIgnoreCase)
                    || text.Contains(removedRoute, StringComparison.OrdinalIgnoreCase);
            })
            .Select(path => Path.GetRelativePath(root, path))
            .Take(20)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ItemCanonicalEndpoints_AreGenericItemEndpointsUnderLibraryItems()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Endpoints\ItemCanonicalEndpoints.cs"));

        Assert.Contains("public static class ItemCanonicalEndpoints", source, StringComparison.Ordinal);
        Assert.Contains("MapItemCanonicalEndpoints", source, StringComparison.Ordinal);
        Assert.Contains(".WithTags(\"Items\")", source, StringComparison.Ordinal);
        Assert.Contains("group.MapPut(\"/{entityId:guid}/preferences\", async (", source, StringComparison.Ordinal);
        Assert.Contains("group.MapPost(\"/{entityId:guid}/canonical-search\", async (", source, StringComparison.Ordinal);
        Assert.Contains("group.MapPost(\"/{entityId:guid}/canonical-apply\", async (", source, StringComparison.Ordinal);
        Assert.DoesNotContain("VaultItemCanonicalEndpoints", source, StringComparison.Ordinal);
        Assert.DoesNotContain("VaultCanonicalSearchRequest", source, StringComparison.Ordinal);
        Assert.DoesNotContain("VaultCanonicalApplyRequest", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ItemCanonicalEndpoints_RouteManualWritesByLineageScope()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Endpoints\ItemCanonicalEndpoints.cs"));

        Assert.Contains("IWorkRepository workRepo", source, StringComparison.Ordinal);
        Assert.Contains("ResolveScopedTarget(context.AssetId, lineage, key)", source, StringComparison.Ordinal);
        Assert.Contains("ResolveScopedTarget(context.AssetId, lineage, kv.Key)", source, StringComparison.Ordinal);
        Assert.Contains("ClaimScopeCatalog.IsParentScoped(key, lineage.MediaType)", source, StringComparison.Ordinal);
        Assert.Contains("COALESCE(ma.id, child_ma.id, grandchild_ma.id)", source, StringComparison.Ordinal);
    }

    private static string GetRepoFilePath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
