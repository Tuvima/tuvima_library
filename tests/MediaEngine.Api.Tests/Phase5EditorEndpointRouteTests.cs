namespace MediaEngine.Api.Tests;

public sealed class Phase5EditorEndpointRouteTests
{
    [Fact]
    public void EditorAndReviewEndpointsExposeRealPhase5Operations()
    {
        var metadata = ReadSource("src/MediaEngine.Api/Endpoints/MetadataEndpoints.cs");
        var navigator = ReadSource("src/MediaEngine.Api/Endpoints/MetadataEndpoints.MediaEditorNavigator.cs");
        var canonical = ReadSource("src/MediaEngine.Api/Endpoints/ItemCanonicalEndpoints.cs");
        var review = ReadSource("src/MediaEngine.Api/Endpoints/ReviewEndpoints.cs");

        Assert.Contains("group.MapGet(\"/{entityId:guid}/editor-context\"", metadata, StringComparison.Ordinal);
        Assert.Contains("group.MapGet(\"/{entityId:guid}/navigator\"", navigator, StringComparison.Ordinal);
        Assert.Contains("/membership-preview", navigator, StringComparison.Ordinal);
        Assert.Contains("/membership-apply", navigator, StringComparison.Ordinal);
        Assert.Contains("/{entityId:guid}/preferences", canonical, StringComparison.Ordinal);
        Assert.Contains("/{entityId:guid}/display-overrides", canonical, StringComparison.Ordinal);
        Assert.Contains("/{entityId:guid}/canonical-search", canonical, StringComparison.Ordinal);
        Assert.Contains("/{entityId:guid}/canonical-apply", canonical, StringComparison.Ordinal);
        Assert.Contains("/{entityId:guid}/retail-match", canonical, StringComparison.Ordinal);
        Assert.Contains("/{entityId:guid}/wikidata-match", canonical, StringComparison.Ordinal);
        Assert.Contains("/{id:guid}/resolve", review, StringComparison.Ordinal);
        Assert.Contains("/{id:guid}/dismiss", review, StringComparison.Ordinal);
        Assert.Contains("/{id:guid}/skip-universe", review, StringComparison.Ordinal);
    }

    [Fact]
    public void MediaEditorNavigator_ReturnsCompactOrdinalsTechnicalBadgesAndClickableState()
    {
        var navigator = ReadSource("src/MediaEngine.Api/Endpoints/MetadataEndpoints.MediaEditorNavigator.cs");
        var metadata = ReadSource("src/MediaEngine.Api/Endpoints/MetadataEndpoints.cs");

        var navigatorService = ReadSource("src/MediaEngine.Api/Services/ReadServices/MediaEditorNavigationReadService.cs");
        Assert.Contains("IMediaEditorNavigationReadService navigationReadService", navigator, StringComparison.Ordinal);
        Assert.Contains("IMediaEditorMembershipReadService membershipReadService", navigator, StringComparison.Ordinal);
        Assert.Contains("compact_ordinal_label", navigatorService, StringComparison.Ordinal);
        Assert.Contains("technical_badges", navigatorService, StringComparison.Ordinal);
        Assert.Contains("primary_asset_id", navigatorService, StringComparison.Ordinal);
        Assert.Contains("is_clickable", navigatorService, StringComparison.Ordinal);
        Assert.Contains("BuildNavigatorTechnicalBadges", navigatorService, StringComparison.Ordinal);
        Assert.Contains("playback_inspection_cache", navigatorService, StringComparison.Ordinal);
        Assert.Contains("file_size_bytes", navigatorService, StringComparison.Ordinal);
        Assert.Contains("video_width", navigatorService, StringComparison.Ordinal);
        Assert.Contains("disc_number", navigatorService, StringComparison.Ordinal);
        Assert.Contains("display_overrides_json", navigatorService, StringComparison.Ordinal);
        Assert.Contains("GetDisplayOverrideValue(value, \"episode_title\", \"title\", \"display_title\")", navigatorService, StringComparison.Ordinal);
        Assert.Contains("GetDisplayOverrideValue(value, \"title\", \"display_title\")", navigatorService, StringComparison.Ordinal);
        Assert.Contains("GetDisplayOverrideValue(value, \"display_subtitle\")", navigatorService, StringComparison.Ordinal);
        Assert.Contains("IsContainerEditorLaunch(launch)", metadata, StringComparison.Ordinal);
        Assert.Contains("!string.Equals(launch.WorkKind, \"child\"", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void EditorLaunchContext_ResolvesCollectionShelvesToCanonicalContainerWork()
    {
        var metadata = ReadSource("src/MediaEngine.Api/Endpoints/MetadataEndpoints.cs");
        var navigatorService = ReadSource("src/MediaEngine.Api/Services/ReadServices/MediaEditorNavigationReadService.cs");

        Assert.Contains("private sealed record EditorLaunchCollectionRow", metadata, StringComparison.Ordinal);
        Assert.Contains("private sealed record EditorLaunchCollectionRow", navigatorService, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN works target ON target.id = COALESCE(gp.id, p.id, w.id)", metadata, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN works target ON target.id = COALESCE(gp.id, p.id, w.id)", navigatorService, StringComparison.Ordinal);
        Assert.Contains("\"Collection\",", metadata, StringComparison.Ordinal);
        Assert.Contains("\"Collection\",", navigatorService, StringComparison.Ordinal);
        Assert.Contains("new { entityId }", metadata, StringComparison.Ordinal);
        Assert.Contains("new { entityId }", navigatorService, StringComparison.Ordinal);
        Assert.Contains("GetRepresentativeAssetForWorkTree(conn, collectionWorkId)", metadata, StringComparison.Ordinal);
        Assert.Contains("GetRepresentativeAssetForWorkTree(conn, collectionWorkId)", navigatorService, StringComparison.Ordinal);
    }

    private static string ReadSource(string relativePath) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), relativePath));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MediaEngine.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
