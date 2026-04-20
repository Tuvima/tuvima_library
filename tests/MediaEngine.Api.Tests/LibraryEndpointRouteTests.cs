using System.IO;

namespace MediaEngine.Api.Tests;

public sealed class LibraryEndpointRouteTests
{
    [Fact]
    public void LibraryEndpoints_ExposeWorkFeedForHomePage()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Endpoints\LibraryEndpoints.cs"));

        Assert.Contains("group.MapGet(\"/works\", async (", source, StringComparison.Ordinal);
        Assert.Contains(".WithName(\"GetLibraryWorks\")", source, StringComparison.Ordinal);
        Assert.Contains(".Produces<List<LibraryWorkListItemDto>>(StatusCodes.Status200OK)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LibraryEndpoints_HomeFeedUsesSharedVisibilityPredicateAndRichArtworkFields()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Endpoints\LibraryEndpoints.cs"));

        Assert.Contains("HomeVisibilitySql.VisibleWorkPredicate(\"w.id\", \"w.curator_state\", \"w.is_catalog_only\")", source, StringComparison.Ordinal);
        Assert.Contains("HomeVisibilitySql.VisibleAssetPathPredicate(\"ad.file_path_root\")", source, StringComparison.Ordinal);
        Assert.Contains("w.collection_id AS collection_id", source, StringComparison.Ordinal);
        Assert.Contains("w.work_kind AS work_kind", source, StringComparison.Ordinal);
        Assert.Contains("root_work_id AS RootWorkId", source, StringComparison.Ordinal);
        Assert.Contains("CoverUrl = coverUrl", source, StringComparison.Ordinal);
        Assert.Contains("BackgroundUrl = backgroundUrl", source, StringComparison.Ordinal);
        Assert.Contains("BannerUrl = bannerUrl", source, StringComparison.Ordinal);
        Assert.Contains("HeroUrl = heroUrl", source, StringComparison.Ordinal);
        Assert.Contains("LogoUrl = logoUrl", source, StringComparison.Ordinal);
    }

    private static string GetRepoFilePath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
