using System.IO;

namespace MediaEngine.Api.Tests;

public sealed class ItemEndpointRouteTests
{
    [Fact]
    public void RegistryEndpoints_UseLibraryItemsRootWithoutDuplicateItemsSegment()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Endpoints\RegistryEndpoints.cs"));

        Assert.Contains("var group = app.MapGroup(\"/library/items\")", source, StringComparison.Ordinal);
        Assert.Contains("group.MapGet(\"\", async (", source, StringComparison.Ordinal);
        Assert.Contains("group.MapGet(\"/{entityId}/detail\", async (", source, StringComparison.Ordinal);
        Assert.Contains("group.MapGet(\"/{entityId:guid}/history\", async (", source, StringComparison.Ordinal);
        Assert.DoesNotContain("group.MapGet(\"/items\", async (", source, StringComparison.Ordinal);
        Assert.DoesNotContain("group.MapGet(\"/items/{entityId}/detail\", async (", source, StringComparison.Ordinal);
        Assert.DoesNotContain("group.MapGet(\"/items/{entityId:guid}/history\", async (", source, StringComparison.Ordinal);
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

    private static string GetRepoFilePath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
