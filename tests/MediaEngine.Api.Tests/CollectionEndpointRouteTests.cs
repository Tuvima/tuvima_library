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
        Assert.Contains("cv_logo_present", source, StringComparison.Ordinal);
        Assert.Contains("THEN '/stream/' || g.first_asset_id || '/logo' END AS logo_url", source, StringComparison.Ordinal);
        Assert.Contains("cover_width_px", source, StringComparison.Ordinal);
        Assert.Contains("DistinctTitleCount = CountDistinctWorkTitles(h.Works)", source, StringComparison.Ordinal);
        Assert.Contains("Description      = row.Description", source, StringComparison.Ordinal);
        Assert.Contains("Tagline          = row.Tagline", source, StringComparison.Ordinal);
        Assert.Contains("Network          = row.Network", source, StringComparison.Ordinal);
        Assert.Contains("SeasonCount      = row.SeasonCount", source, StringComparison.Ordinal);
        Assert.Contains("LogoUrl          = row.LogoUrl", source, StringComparison.Ordinal);
        Assert.Contains("MapGet(\"/{id:guid}/square-artwork\"", source, StringComparison.Ordinal);
        Assert.Contains("MapPost(\"/{id:guid}/square-artwork\"", source, StringComparison.Ordinal);
        Assert.Contains("MapDelete(\"/{id:guid}/square-artwork\"", source, StringComparison.Ordinal);
        Assert.Contains("UpdateCollectionSquareArtworkAsync(id, targetPath, mimeType", source, StringComparison.Ordinal);
        Assert.Contains("UpdateCollectionSquareArtworkAsync(id, null, null", source, StringComparison.Ordinal);

        var accessPolicySource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Models\CollectionAccessPolicy.cs"));
        Assert.Contains("Smart", accessPolicySource, StringComparison.Ordinal);
        Assert.Contains("PlaylistFolder", accessPolicySource, StringComparison.Ordinal);
    }

    private static string GetRepoFilePath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
