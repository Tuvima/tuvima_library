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
            $"{Path.DirectorySeparatorChar}TestResults{Path.DirectorySeparatorChar}",
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
                    && !relative.StartsWith(@"src\MediaEngine.Domain\Capabilities\", StringComparison.OrdinalIgnoreCase)
                    && !relative.Equals(@"src\MediaEngine.Api\Services\ReviewQueueRouter.cs", StringComparison.OrdinalIgnoreCase)
                    && !relative.Equals(@"src\MediaEngine.Api\Program.cs", StringComparison.OrdinalIgnoreCase)
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
        Assert.Contains("group.MapGet(\"/{entityId:guid}/editor-preferences/{profileId:guid}\", async (", source, StringComparison.Ordinal);
        Assert.Contains("group.MapPut(\"/{entityId:guid}/editor-preferences/{profileId:guid}\", async (", source, StringComparison.Ordinal);
        Assert.Contains("group.MapPost(\"/{entityId:guid}/canonical-search\", async (", source, StringComparison.Ordinal);
        Assert.Contains("group.MapPost(\"/{entityId:guid}/canonical-apply\", async (", source, StringComparison.Ordinal);
        Assert.Contains("group.MapPost(\"/{entityId:guid}/retail-match\", async (", source, StringComparison.Ordinal);
        Assert.Contains("group.MapPost(\"/{entityId:guid}/wikidata-match\", async (", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ItemEditorEndpoints_ResolveCurrentMediaAssetOrWorkTargets()
    {
        var canonical = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Endpoints\ItemCanonicalEndpoints.cs"));
        var canonicalData = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Services\ItemCanonicalDataService.cs"));
        var libraryItems = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Endpoints\LibraryItemEndpoints.cs"));
        var libraryItemData = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Services\LibraryItemCurationStore.cs"));

        Assert.Contains("ResolveWorkAssetContextAsync", canonical, StringComparison.Ordinal);
        Assert.Contains("No current media asset or work target found", canonical, StringComparison.Ordinal);
        Assert.Contains("GuidSql.ToBlob(entityId)", canonicalData, StringComparison.Ordinal);
        Assert.Contains("store.ResolveTargetAsync(entityId, ct)", libraryItems, StringComparison.Ordinal);
        Assert.Contains("WHERE ma.id = @entityId", libraryItemData, StringComparison.Ordinal);
        Assert.Contains("OR e.work_id = @entityId", libraryItemData, StringComparison.Ordinal);
        Assert.DoesNotContain("No media asset found for work", canonical, StringComparison.Ordinal);
        Assert.DoesNotContain("No media asset found for work", libraryItems, StringComparison.Ordinal);
    }

    [Fact]
    public void ItemCanonicalEndpoints_RouteManualWritesByLineageScope()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Endpoints\ItemCanonicalEndpoints.cs"));
        var canonicalData = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Services\ItemCanonicalDataService.cs"));

        Assert.Contains("IWorkRepository workRepo", source, StringComparison.Ordinal);
        Assert.Contains("ResolveScopedTarget(context.AssetId, lineage, key)", source, StringComparison.Ordinal);
        Assert.Contains("ResolveScopedTarget(context.AssetId, lineage, kv.Key)", source, StringComparison.Ordinal);
        Assert.Contains("ClaimScopeCatalog.IsParentScoped(key, lineage.MediaType)", source, StringComparison.Ordinal);
        Assert.Contains("COALESCE(ma.id, child_ma.id, grandchild_ma.id)", canonicalData, StringComparison.Ordinal);
    }

    [Fact]
    public void ItemCanonicalEndpoints_UseTypedDataServiceInsteadOfDirectDatabaseAccess()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Endpoints\ItemCanonicalEndpoints.cs"));

        Assert.Contains("IItemCanonicalDataService itemCanonicalData", source, StringComparison.Ordinal);
        Assert.Contains("itemCanonicalData.ResolveWorkAssetContextAsync", source, StringComparison.Ordinal);
        Assert.Contains("itemCanonicalData.DeleteIdentityArtifactsAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IDatabaseConnection", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateConnection", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GuidSql", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ItemCanonicalEndpoints_SupportRetailAndWikidataSearchModes()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Endpoints\ItemCanonicalEndpoints.cs"));
        var models = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Models\ItemCanonicalModels.cs"));

        Assert.Contains("SearchMode", models, StringComparison.Ordinal);
        Assert.Contains("\"retail_only\"", source, StringComparison.Ordinal);
        Assert.Contains("\"wikidata_only\"", source, StringComparison.Ordinal);
        Assert.Contains("\"combined\"", source, StringComparison.Ordinal);
        Assert.Contains("shouldSearchRetail", source, StringComparison.Ordinal);
        Assert.Contains("shouldSearchUniverse", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ItemCanonicalEndpoints_ManualWikidataReplacementQueuesAssetEnrichment()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Endpoints\ItemCanonicalEndpoints.cs"));

        Assert.DoesNotContain("pipeline.RunSynchronousAsync(new HarvestRequest", source, StringComparison.Ordinal);
        Assert.Contains("await pipeline.EnqueueAsync(new HarvestRequest", source, StringComparison.Ordinal);
        Assert.Contains("EntityId = context.AssetId", source, StringComparison.Ordinal);
        Assert.Contains("MediaType = ToMediaType(context.MediaType)", source, StringComparison.Ordinal);
        Assert.Contains("IsUserResolution = true", source, StringComparison.Ordinal);
        Assert.Contains("Wikidata identity replaced; enrichment queued.", source, StringComparison.Ordinal);
    }

    private static string GetRepoFilePath(
        string relativePath,
        [System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "")
    {
        var directory = !string.IsNullOrWhiteSpace(sourceFile)
            ? new DirectoryInfo(Path.GetDirectoryName(sourceFile)!)
            : new DirectoryInfo(Directory.GetCurrentDirectory());

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MediaEngine.slnx")))
            directory = directory.Parent;

        if (directory is not null)
            return Path.GetFullPath(Path.Combine(directory.FullName, relativePath));

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
    }
}
