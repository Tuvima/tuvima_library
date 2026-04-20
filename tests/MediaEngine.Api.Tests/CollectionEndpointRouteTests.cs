using System.IO;

namespace MediaEngine.Api.Tests;

public sealed class CollectionEndpointRouteTests
{
    [Fact]
    public void CollectionEndpoints_GroupFeedsUseSharedVisibilityRulesAndRichMetadata()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Endpoints\CollectionEndpoints.cs"));

        Assert.Contains("HomeVisibilitySql.VisibleAssetPathPredicate(\"ma.file_path_root\")", source, StringComparison.Ordinal);
        Assert.Contains("HomeVisibilitySql.VisibleWorkPredicate(\"w.id\", \"w.curator_state\", \"w.is_catalog_only\")", source, StringComparison.Ordinal);
        Assert.Contains("'/stream/' || g.first_asset_id || '/logo' AS logo_url", source, StringComparison.Ordinal);
        Assert.Contains("Description      = row.Description", source, StringComparison.Ordinal);
        Assert.Contains("Tagline          = row.Tagline", source, StringComparison.Ordinal);
        Assert.Contains("Network          = row.Network", source, StringComparison.Ordinal);
        Assert.Contains("SeasonCount      = row.SeasonCount", source, StringComparison.Ordinal);
        Assert.Contains("LogoUrl          = row.LogoUrl", source, StringComparison.Ordinal);
    }

    private static string GetRepoFilePath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
